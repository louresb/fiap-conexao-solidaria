using ConexaoSolidaria.Contracts.Auth;
using ConexaoSolidaria.Contracts.Donations;
using ConexaoSolidaria.Donations.Api.Campaigns;
using ConexaoSolidaria.Donations.Api.Consumers;
using ConexaoSolidaria.Donations.Api.Data;
using ConexaoSolidaria.Donations.Api.Features.CreateDonation;
using ConexaoSolidaria.ServiceDefaults.Http;
using ConexaoSolidaria.ServiceDefaults.Observability;
using ConexaoSolidaria.ServiceDefaults.OpenApi;

using MassTransit;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

using Prometheus;

using Scalar.AspNetCore;

using Serilog;
using Serilog.Events;
using Serilog.Sinks.Grafana.Loki;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, configuration) =>
{
    configuration
        .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Service", "donations-api")
        .WriteTo.Console();

    var lokiUrl = context.Configuration["Observability:LokiUrl"];
    if (!string.IsNullOrWhiteSpace(lokiUrl))
    {
        configuration.WriteTo.GrafanaLoki(lokiUrl);
    }
});

builder.Services.AddConexaoSolidariaTelemetry(
    builder.Configuration,
    builder.Environment,
    "donations-api");

builder.Services.AddDbContext<DonationsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DonationsDb")));
builder.Services.AddScoped<CreateDonationHandler>();
builder.Services.AddHttpClient<ICampaignEligibilityGateway, CampaignEligibilityClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["Services:CampaignsBaseUrl"] ?? "http://localhost:5102");
    client.Timeout = TimeSpan.FromSeconds(5);
});

builder.Services.AddMassTransit(bus =>
{
    bus.AddConsumer<PaymentConfirmedConsumer>();
    bus.AddEntityFrameworkOutbox<DonationsDbContext>(outbox =>
    {
        outbox.UsePostgres();
        outbox.UseBusOutbox();
        outbox.DuplicateDetectionWindow = TimeSpan.FromHours(24);
    });

    bus.UsingRabbitMq((context, cfg) =>
    {
        cfg.Host(
            builder.Configuration["RabbitMq:Host"] ?? "localhost",
            builder.Configuration["RabbitMq:VirtualHost"] ?? "/",
            host =>
            {
                host.Username(builder.Configuration["RabbitMq:Username"] ?? "guest");
                host.Password(builder.Configuration["RabbitMq:Password"] ?? string.Empty);
            });

        cfg.ReceiveEndpoint("donations-api", endpoint =>
        {
            endpoint.UseMessageRetry(retry => retry.Intervals(200, 500, 1000, 5000));
            endpoint.UseEntityFrameworkOutbox<DonationsDbContext>(context);
            endpoint.ConcurrentMessageLimit = 8;
            endpoint.ConfigureConsumer<PaymentConfirmedConsumer>(context);
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
        options.RequireHttpsMetadata = builder.Configuration.GetValue("Auth:RequireHttpsMetadata", false);
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
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Donors", policy =>
        policy.RequireRole(AuthDefaults.DonorRole));
    options.AddPolicy("DonorsOrManagers", policy =>
        policy.RequireRole(AuthDefaults.DonorRole, AuthDefaults.ManagerRole));
});
builder.Services.AddConexaoSolidariaOpenApi(
    "Conexao Solidaria - Donations API",
    "Registro e acompanhamento do ciclo de doacoes.");

var app = builder.Build();
var enforceAuth = app.Configuration.GetValue("Auth:Enforce", true);

app.UseAuthentication();
app.UseCorrelationAndTenant();
app.UseAuthorization();
app.UseHttpMetrics();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy", service = "donations-api" }))
    .WithTags("Operacao")
    .WithName("DonationsLiveness")
    .WithSummary("Verifica se a Donations API esta em execucao.");
app.MapMetrics();
app.MapGet("/health/ready", async (DonationsDbContext db, CancellationToken cancellationToken) =>
{
    await db.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
    return Results.Ok(new { status = "Healthy", dependencies = new[] { "postgres", "rabbitmq", "campaigns-api" } });
})
    .WithTags("Operacao")
    .WithName("DonationsReadiness")
    .WithSummary("Verifica as dependencias criticas da Donations API.");

var donations = app.MapGroup("/api/donations").WithTags("Doacoes");
if (enforceAuth)
{
    donations.RequireAuthorization("DonorsOrManagers");
}

donations.MapPost("/", async (
    CreateDonationRequest request,
    CreateDonationHandler handler,
    HttpContext http,
    CancellationToken cancellationToken) =>
{
    var command = new CreateDonationCommand(
        request,
        http.TenantId(),
        http.CorrelationId(),
        http.User.FindFirst("sub")?.Value,
        http.User.FindFirst("email")?.Value);
    var result = await handler.HandleAsync(command, cancellationToken);

    return result.Status switch
    {
        CreateDonationStatus.Created => Results.Accepted(
            $"/api/donations/{result.Donation!.Id}",
            new { result.Donation.Id, result.Donation.Status }),
        CreateDonationStatus.CampaignNotFound => Results.NotFound(new { error = result.Error }),
        CreateDonationStatus.CampaignUnavailable => Results.Conflict(new { error = result.Error }),
        _ => Results.ValidationProblem(result.ValidationErrors!)
    };
})
    .WithName("CreateDonation")
    .WithSummary("Registra uma doacao e publica a intencao de pagamento pela Outbox.");

var donorHistory = donations.MapGet("/mine", async (
    DonationsDbContext db,
    HttpContext http,
    CancellationToken cancellationToken) =>
{
    var donorId = http.User.FindFirst("sub")?.Value;
    if (string.IsNullOrWhiteSpace(donorId))
    {
        return Results.Unauthorized();
    }

    var history = await db.Donations
        .AsNoTracking()
        .Where(item => item.TenantId == http.TenantId() && item.DonorId == donorId)
        .OrderByDescending(item => item.CreatedAtUtc)
        .Take(100)
        .ToListAsync(cancellationToken);

    return Results.Ok(history.Select(ToDto));
})
    .WithName("ListMyDonations")
    .WithSummary("Lista exclusivamente as doacoes do usuario autenticado no tenant atual.");

if (enforceAuth)
{
    donorHistory.RequireAuthorization("Donors");
}

donations.MapGet("/{id:guid}", async (
    Guid id,
    DonationsDbContext db,
    HttpContext http,
    CancellationToken cancellationToken) =>
{
    var donation = await db.Donations.AsNoTracking().FirstOrDefaultAsync(
        item => item.Id == id && item.TenantId == http.TenantId(),
        cancellationToken);
    return donation is null ? Results.NotFound() : Results.Ok(ToDto(donation));
})
    .WithName("GetDonation")
    .WithSummary("Consulta uma doacao dentro do tenant atual.");

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<DonationsDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();

static DonationDto ToDto(Donation donation) => new(
    donation.Id,
    donation.CampaignId,
    string.IsNullOrWhiteSpace(donation.CampaignTitle) ? "Campanha apoiada" : donation.CampaignTitle,
    donation.TenantId,
    donation.DonorId,
    donation.DonorEmail,
    donation.Amount,
    donation.Currency,
    donation.PaymentMethod,
    donation.Status,
    donation.CreatedAtUtc,
    donation.ConfirmedAtUtc);

public partial class Program;
