using ConexaoSolidaria.Campaigns.Data;
using ConexaoSolidaria.Donations.Worker.Consumers;

using MassTransit;

using Microsoft.AspNetCore.Diagnostics.HealthChecks;
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
        .Enrich.WithProperty("Service", "donations-worker")
        .WriteTo.Console();

    var lokiUrl = context.Configuration["Observability:LokiUrl"];
    if (!string.IsNullOrWhiteSpace(lokiUrl))
    {
        configuration.WriteTo.GrafanaLoki(lokiUrl);
    }
});

builder.Services.AddDbContext<CampaignsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("CampaignsDb")));

builder.Services.AddMassTransit(bus =>
{
    bus.AddConsumer<PaymentConfirmedConsumer>();
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

        cfg.ReceiveEndpoint("donations-worker", endpoint =>
        {
            endpoint.UseMessageRetry(retry => retry.Intervals(200, 500, 1000, 5000));
            endpoint.UseEntityFrameworkOutbox<CampaignsDbContext>(context);
            endpoint.ConcurrentMessageLimit = 8;
            endpoint.ConfigureConsumer<PaymentConfirmedConsumer>(context);
        });
    });
});
builder.Services.AddHealthChecks();

var app = builder.Build();

app.UseHttpMetrics();
app.MapMetrics();
app.MapGet("/health/live", () => Results.Ok(new { status = "Healthy", service = "donations-worker" }));
app.MapHealthChecks("/health/ready", new HealthCheckOptions());

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CampaignsDbContext>();
    await CampaignsDbContext.SeedDemoDataAsync(db);
}

await app.RunAsync();