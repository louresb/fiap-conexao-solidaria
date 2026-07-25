using ConexaoSolidaria.Contracts.Auth;
using ConexaoSolidaria.Contracts.Payments;
using ConexaoSolidaria.Infrastructure.Http;
using ConexaoSolidaria.Payments.Api.Consumers;
using ConexaoSolidaria.Payments.Api.Data;
using ConexaoSolidaria.Payments.Api.Providers;
using ConexaoSolidaria.Payments.Api.Services;
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

builder.Services.AddDbContext<PaymentsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PaymentsDb")));

builder.Services.AddOptions<FakePaymentOptions>()
    .Bind(builder.Configuration.GetSection("FakePayment"));
builder.Services.AddSingleton<IFakePaymentProvider, FakePaymentProvider>();
builder.Services.AddScoped<PaymentConfirmationService>();

builder.Services.AddMassTransit(bus =>
{
    bus.AddConsumer<DonationIntentCreatedConsumer>();
    bus.AddEntityFrameworkOutbox<PaymentsDbContext>(outbox =>
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

        cfg.ReceiveEndpoint("payments-api", endpoint =>
        {
            endpoint.UseMessageRetry(retry => retry.Intervals(200, 500, 1000, 5000));
            endpoint.UseEntityFrameworkOutbox<PaymentsDbContext>(context);
            endpoint.ConfigureConsumer<DonationIntentCreatedConsumer>(context);
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

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("DonorsOrManagers", policy =>
        policy.RequireRole(AuthDefaults.DonorRole, AuthDefaults.ManagerRole));
});
builder.Services.AddOpenApi();

var app = builder.Build();
var enforceAuth = app.Configuration.GetValue("Auth:Enforce", true);

app.UseAuthentication();
app.UseCorrelationAndTenant();
app.UseAuthorization();
app.UseHttpMetrics();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy", service = "payments-api" }));
app.MapMetrics();
app.MapGet("/health/ready", async (PaymentsDbContext db, CancellationToken cancellationToken) =>
{
    await db.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
    return Results.Ok(new { status = "Healthy", dependencies = new[] { "postgres", "rabbitmq" } });
});

var payments = app.MapGroup("/api/payments");
if (enforceAuth)
{
    payments.RequireAuthorization("DonorsOrManagers");
}

payments.MapGet("/donations/{donationId:guid}", async (
    Guid donationId,
    PaymentsDbContext db,
    HttpContext http,
    CancellationToken cancellationToken) =>
{
    var tenantId = http.TenantId();
    var payment = await db.Payments.AsNoTracking().FirstOrDefaultAsync(
        item => item.DonationId == donationId && item.TenantId == tenantId,
        cancellationToken);

    return payment is null ? Results.NotFound() : Results.Ok(ToDto(payment));
});

payments.MapPost("/{paymentId:guid}/simulate-confirmation", async (
    Guid paymentId,
    PaymentsDbContext db,
    PaymentConfirmationService confirmation,
    HttpContext http,
    CancellationToken cancellationToken) =>
{
    var tenantId = http.TenantId();
    var payment = await db.Payments.FirstOrDefaultAsync(
        item => item.Id == paymentId && item.TenantId == tenantId,
        cancellationToken);

    if (payment is null)
    {
        return Results.NotFound();
    }

    var result = await confirmation.ConfirmAsync(
        payment,
        $"simulation-{Guid.NewGuid():N}",
        http.CorrelationId(),
        cancellationToken);

    return Results.Ok(new { status = result.ToString(), payment = ToDto(payment) });
});

app.MapPost("/api/payment-webhooks/fake", async (
    FakePaymentWebhookRequest request,
    PaymentsDbContext db,
    IFakePaymentProvider provider,
    PaymentConfirmationService confirmation,
    HttpContext http,
    CancellationToken cancellationToken) =>
{
    var signature = http.Request.Headers["X-Webhook-Signature"].FirstOrDefault() ?? string.Empty;
    var canonicalPayload = $"{request.ProviderEventId}|{request.ProviderPaymentId}|{request.Status.ToLowerInvariant()}";
    if (!provider.IsValidSignature(canonicalPayload, signature))
    {
        return Results.Unauthorized();
    }

    if (!string.Equals(request.Status, "approved", StringComparison.OrdinalIgnoreCase))
    {
        return Results.Accepted(value: new { status = "ignored" });
    }

    var payment = await db.Payments.FirstOrDefaultAsync(
        item => item.ProviderPaymentId == request.ProviderPaymentId,
        cancellationToken);
    if (payment is null)
    {
        return Results.NotFound();
    }

    var result = await confirmation.ConfirmAsync(
        payment,
        request.ProviderEventId,
        http.CorrelationId(),
        cancellationToken);
    return Results.Ok(new { status = result.ToString() });
}).AllowAnonymous();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<PaymentsDbContext>();
    await db.Database.EnsureCreatedAsync();
}

app.Run();

static PaymentIntentDto ToDto(PaymentIntent payment) => new(
    payment.Id,
    payment.DonationId,
    payment.CampaignId,
    payment.TenantId,
    payment.Amount,
    payment.Currency,
    payment.PaymentMethod,
    payment.Provider,
    payment.ProviderPaymentId,
    payment.Status,
    payment.QrCodePayload,
    payment.ExpiresAtUtc,
    payment.ConfirmedAtUtc);

public partial class Program;
