using ConexaoSolidaria.Campaigns.Data;
using ConexaoSolidaria.Campaigns.Infrastructure.Search;
using ConexaoSolidaria.Campaigns.Worker.Consumers;
using ConexaoSolidaria.ServiceDefaults.Http;
using ConexaoSolidaria.ServiceDefaults.Observability;

using MassTransit;

using Microsoft.EntityFrameworkCore;

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
        .Enrich.WithProperty("Service", "campaigns-worker")
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
    "campaigns-worker");

builder.Services.AddDbContext<CampaignsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("CampaignsDb")));
builder.Services.AddStackExchangeRedisCache(options =>
    options.Configuration = builder.Configuration.GetConnectionString("Redis"));
builder.Services.Configure<OpenSearchOptions>(builder.Configuration.GetSection("OpenSearch"));
builder.Services.AddHttpClient<OpenSearchCampaignSearchIndexer>();
builder.Services.AddScoped<ICampaignSearchIndexer, OpenSearchCampaignSearchIndexer>();

builder.Services.AddMassTransit(bus =>
{
    bus.AddConsumer<DonationProcessedConsumer>();
    bus.AddConsumer<CampaignChangedConsumer>();
    bus.AddEntityFrameworkOutbox<CampaignsDbContext>(outbox =>
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

        cfg.ReceiveEndpoint("campaign-projections", endpoint =>
        {
            endpoint.UseMessageRetry(retry => retry.Intervals(200, 500, 1000, 5000));
            endpoint.UseEntityFrameworkOutbox<CampaignsDbContext>(context);
            endpoint.ConcurrentMessageLimit = 8;
            endpoint.ConfigureConsumer<DonationProcessedConsumer>(context);
            endpoint.ConfigureConsumer<CampaignChangedConsumer>(context);
        });
    });
});

var app = builder.Build();

app.UseCorrelationAndTenant();
app.UseHttpMetrics();
app.MapMetrics();
app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy", service = "campaigns-worker" }));
app.MapGet("/health/ready", async (CampaignsDbContext db, CancellationToken cancellationToken) =>
{
    await db.Database.ExecuteSqlRawAsync("SELECT 1", cancellationToken);
    return Results.Ok(new { status = "Healthy", dependencies = new[] { "postgres", "rabbitmq", "redis", "opensearch" } });
});

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CampaignsDbContext>();
    await db.Database.MigrateAsync();
}

app.Run();

public partial class Program;
