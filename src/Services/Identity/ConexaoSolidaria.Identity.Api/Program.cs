using System.Text.Json;
using BCrypt.Net;
using ConexaoSolidaria.Contracts.Auth;
using ConexaoSolidaria.Contracts.Events;
using ConexaoSolidaria.Contracts.Identity;
using ConexaoSolidaria.Contracts.Validation;
using ConexaoSolidaria.Identity.Api.Data;
using ConexaoSolidaria.Identity.Api.Keycloak;
using ConexaoSolidaria.Infrastructure.Http;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Prometheus;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Grafana.Loki;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
{
    configuration
        .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
        .Enrich.FromLogContext()
        .WriteTo.Console();

    var lokiUrl = context.Configuration["Observability:LokiUrl"];
    if (!string.IsNullOrWhiteSpace(lokiUrl))
    {
        configuration.WriteTo.GrafanaLoki(lokiUrl);
    }
});

builder.Services.AddDbContext<IdentityDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("IdentityDb")));

builder.Services.Configure<KeycloakOptions>(builder.Configuration.GetSection("Keycloak"));
builder.Services.AddHttpClient<KeycloakUserProvisioner>();
builder.Services.AddScoped<IKeycloakUserProvisioner>(sp =>
{
    var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<KeycloakOptions>>().Value;
    return options.Enabled
        ? sp.GetRequiredService<KeycloakUserProvisioner>()
        : new NoopKeycloakUserProvisioner();
});

builder.Services.AddMassTransit(bus =>
{
    bus.AddEntityFrameworkOutbox<IdentityDbContext>(outbox =>
    {
        outbox.UsePostgres();
        outbox.UseBusOutbox();
    });

    bus.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(
            builder.Configuration["RabbitMq:Host"] ?? "localhost",
            builder.Configuration["RabbitMq:VirtualHost"] ?? "/",
            host =>
            {
                host.Username(builder.Configuration["RabbitMq:Username"] ?? "guest");
                host.Password(builder.Configuration["RabbitMq:Password"] ?? "guest");
            });
    });
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var authority = builder.Configuration["Auth:Authority"]
            ?? throw new InvalidOperationException("Auth:Authority is required.");
        var audience = builder.Configuration["Auth:Audience"] ?? "conexao-solidaria";
        options.MapInboundClaims = false;
        options.Authority = authority;
        options.Audience = audience;
        options.RequireHttpsMetadata = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Auth:Issuer"] ?? authority,
            RoleClaimType = "roles",
            NameClaimType = "preferred_username"
        };
    });

builder.Services.AddAuthorization();
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseAuthentication();
app.UseCorrelationAndTenant();
app.UseAuthorization();
app.UseHttpMetrics();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy", service = "identity-api" }));
app.MapMetrics();
app.MapGet("/health/ready", async (IdentityDbContext db, CancellationToken cancellationToken) =>
{
    await db.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
    return Results.Ok(new { status = "Healthy", dependencies = new[] { "postgres" } });
});

app.MapPost("/api/donors/register", async (
    DonorRegistrationRequest request,
    IdentityDbContext db,
    IPublishEndpoint publishEndpoint,
    IKeycloakUserProvisioner keycloak,
    HttpContext http,
    CancellationToken cancellationToken) =>
{
    var tenantId = http.TenantId();
    var normalizedEmail = request.Email.Trim().ToLowerInvariant();
    var normalizedCpf = CpfValidator.Normalize(request.Cpf);

    if (request.FullName.Trim().Length < 3 || !System.Net.Mail.MailAddress.TryCreate(normalizedEmail, out _))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["registration"] = ["Nome completo e email valido sao obrigatorios."]
        });
    }

    if (!CpfValidator.IsValid(normalizedCpf))
    {
        return Results.BadRequest(new { error = "CPF invalido." });
    }

    if (request.Password.Length < 10
        || !request.Password.Any(char.IsUpper)
        || !request.Password.Any(char.IsLower)
        || !request.Password.Any(char.IsDigit))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["password"] = ["A senha deve ter ao menos 10 caracteres, com letras maiusculas, minusculas e numeros."]
        });
    }

    var exists = await db.Donors.AnyAsync(d => d.TenantId == tenantId && d.Email == normalizedEmail, cancellationToken);
    if (exists)
    {
        return Results.Conflict(new { error = "Email ja cadastrado para este tenant." });
    }

    var donor = new Donor
    {
        TenantId = tenantId,
        FullName = request.FullName.Trim(),
        Email = normalizedEmail,
        Cpf = normalizedCpf,
        PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password)
    };

    await keycloak.ProvisionDonorAsync(request with { Email = normalizedEmail, Cpf = normalizedCpf }, tenantId, cancellationToken);
    db.Donors.Add(donor);

    var payload = JsonSerializer.Serialize(new
    {
        donor.Id,
        donor.TenantId,
        donor.FullName,
        donor.Email
    });

    await publishEndpoint.Publish(
        IntegrationEvent.Create(EventTypes.DonorRegistered, tenantId, http.CorrelationId(), "identity-api", payload),
        cancellationToken);

    await db.SaveChangesAsync(cancellationToken);

    return Results.Created($"/api/donors/{donor.Id}", new DonorProfileDto(donor.Id, donor.TenantId, donor.FullName, donor.Email, MaskCpf(donor.Cpf)));
});

app.MapGet("/api/donors/{id:guid}", async (Guid id, IdentityDbContext db, HttpContext http, CancellationToken cancellationToken) =>
{
    var tenantId = http.TenantId();
    var donor = await db.Donors.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id && d.TenantId == tenantId, cancellationToken);
    return donor is null
        ? Results.NotFound()
        : Results.Ok(new DonorProfileDto(donor.Id, donor.TenantId, donor.FullName, donor.Email, MaskCpf(donor.Cpf)));
}).RequireAuthorization();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.Run();

static string MaskCpf(string cpf) => cpf.Length == 11
    ? $"***.{cpf[3..6]}.{cpf[6..9]}-**"
    : "***";
