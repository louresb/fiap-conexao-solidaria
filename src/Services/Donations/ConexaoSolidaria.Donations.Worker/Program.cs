using ConexaoSolidaria.Campaigns.Data;
using ConexaoSolidaria.Donations.Worker.Consumers;
using MassTransit;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Serilog.Events;
using Serilog.Sinks.Grafana.Loki;

var builder = Host.CreateApplicationBuilder(args);

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

builder.Services.AddSerilog((services, configuration) =>
{
    configuration
        .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
        .Enrich.FromLogContext()
        .WriteTo.Console();

    var config = services.GetRequiredService<IConfiguration>();
    var lokiUrl = config["Observability:LokiUrl"];
    if (!string.IsNullOrWhiteSpace(lokiUrl))
    {
        configuration.WriteTo.GrafanaLoki(lokiUrl);
    }
});

builder.Services.AddDbContext<CampaignsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("CampaignsDb")));

var redisConnection = builder.Configuration.GetConnectionString("Redis");
if (string.IsNullOrWhiteSpace(redisConnection))
{
    builder.Services.AddDistributedMemoryCache();
}
else
{
    builder.Services.AddStackExchangeRedisCache(options => options.Configuration = redisConnection);
}

builder.Services.AddMassTransit(bus =>
{
    bus.AddConsumer<DonationIntentCreatedConsumer>();
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
                host.Password(builder.Configuration["RabbitMq:Password"] ?? "guest");
            });

        cfg.ReceiveEndpoint("donations-worker", endpoint =>
        {
            endpoint.UseMessageRetry(retry => retry.Intervals(200, 500, 1000, 5000));
            endpoint.UseEntityFrameworkOutbox<CampaignsDbContext>(context);
            endpoint.ConcurrentMessageLimit = 8;
            endpoint.ConfigureConsumer<DonationIntentCreatedConsumer>(context);
        });
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<CampaignsDbContext>();
    await CampaignsDbContext.SeedDemoDataAsync(db);
}

await app.RunAsync();
