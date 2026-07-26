using ConexaoSolidaria.Audit.Api.Consumers;
using ConexaoSolidaria.Audit.Api.Data;
using ConexaoSolidaria.Contracts.Audit;
using ConexaoSolidaria.Contracts.Auth;
using ConexaoSolidaria.Infrastructure.Http;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
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

builder.Services.AddSingleton<AuditMongoContext>();
builder.Services.AddMassTransit(bus =>
{
    bus.AddConsumer<AuditIntegrationEventConsumer>();
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

        cfg.ReceiveEndpoint("audit-api", endpoint =>
        {
            endpoint.UseMessageRetry(retry => retry.Intervals(200, 500, 1000, 5000));
            endpoint.ConfigureConsumer<AuditIntegrationEventConsumer>(context);
        });
    });
});

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var authority = builder.Configuration["Auth:Authority"]
            ?? throw new InvalidOperationException("Auth:Authority is required.");
        var audience = builder.Configuration["Auth:Audience"] ?? "conexao-solidaria";
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
    options.AddPolicy("ManagersOnly", policy => policy.RequireRole(AuthDefaults.ManagerRole)));
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

app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy", service = "audit-api" }));
app.MapMetrics();
app.MapGet("/health/ready", async (AuditMongoContext mongo, CancellationToken cancellationToken) =>
{
    await mongo.Database.RunCommandAsync((Command<MongoDB.Bson.BsonDocument>)"{ping:1}", cancellationToken: cancellationToken);
    return Results.Ok(new { status = "Healthy", dependencies = new[] { "mongodb", "rabbitmq" } });
});

var audit = app.MapGroup("/api/audit").RequireAuthorization("ManagersOnly");

audit.MapGet("/", async (
    int? limit,
    AuditMongoContext mongo,
    HttpContext http,
    CancellationToken cancellationToken) =>
{
    var resolvedTenant = http.TenantId();
    var take = Math.Clamp(limit ?? 25, 1, 100);
    var filter = Builders<AuditEventDocument>.Filter.Eq(e => e.TenantId, resolvedTenant);
    var documents = await mongo.Events
        .Find(filter)
        .SortByDescending(e => e.Timestamp)
        .Limit(take)
        .ToListAsync(cancellationToken);

    return Results.Ok(documents.Select(d => new AuditEventDto(
        d.Id.ToString(),
        d.TenantId,
        d.EventType,
        d.Source,
        d.CorrelationId,
        d.CausationId,
        d.PayloadJson,
        d.Timestamp)));
});

audit.MapGet("/{correlationId}", async (
    string correlationId,
    AuditMongoContext mongo,
    HttpContext http,
    CancellationToken cancellationToken) =>
{
    var filter = Builders<AuditEventDocument>.Filter.And(
        Builders<AuditEventDocument>.Filter.Eq(e => e.TenantId, http.TenantId()),
        Builders<AuditEventDocument>.Filter.Eq(e => e.CorrelationId, correlationId));
    var documents = await mongo.Events
        .Find(filter)
        .SortBy(e => e.Timestamp)
        .ToListAsync(cancellationToken);

    return Results.Ok(documents.Select(d => new AuditEventDto(
        d.Id.ToString(),
        d.TenantId,
        d.EventType,
        d.Source,
        d.CorrelationId,
        d.CausationId,
        d.PayloadJson,
        d.Timestamp)));
});

await app.Services.GetRequiredService<AuditMongoContext>().EnsureIndexesAsync();

app.Run();
