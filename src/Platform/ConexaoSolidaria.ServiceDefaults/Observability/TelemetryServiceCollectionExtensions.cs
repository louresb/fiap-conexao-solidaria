using ConexaoSolidaria.ServiceDefaults.Http;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ConexaoSolidaria.ServiceDefaults.Observability;

public static class TelemetryServiceCollectionExtensions
{
    private const string ServiceNamespace = "ConexaoSolidaria";

    public static IServiceCollection AddConexaoSolidariaTelemetry(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment,
        string serviceName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceName);

        var endpointValue = configuration["Observability:OtlpEndpoint"];
        var hasEndpoint = Uri.TryCreate(endpointValue, UriKind.Absolute, out var endpoint);

        services
            .AddOpenTelemetry()
            .ConfigureResource(resource => resource
                .AddService(
                    serviceName: serviceName,
                    serviceNamespace: ServiceNamespace,
                    serviceVersion: typeof(TelemetryServiceCollectionExtensions).Assembly.GetName().Version?.ToString())
                .AddAttributes(
                [
                    new("deployment.environment.name", configuration["CS_ENVIRONMENT"] ?? environment.EnvironmentName),
                    new("cloud.provider", configuration["CS_CLOUD_PROVIDER"] ?? "local"),
                    new("service.instance.id", Environment.GetEnvironmentVariable("HOSTNAME") ?? Environment.MachineName)
                ]))
            .WithTracing(tracing =>
            {
                tracing
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.Filter = context => !IsNoiseEndpoint(context.Request.Path);
                        options.EnrichWithHttpRequest = (activity, request) =>
                        {
                            if (request.Headers.TryGetValue(CorrelationTenantMiddleware.CorrelationHeader, out var correlationId))
                            {
                                activity.SetTag("app.correlation_id", correlationId.ToString());
                            }

                            if (request.Headers.TryGetValue(CorrelationTenantMiddleware.TenantHeader, out var tenantId))
                            {
                                activity.SetTag("app.tenant.id", tenantId.ToString());
                            }
                        };
                    })
                    .AddHttpClientInstrumentation()
                    .AddSource("MassTransit");

                if (hasEndpoint)
                {
                    tracing.AddOtlpExporter(options =>
                    {
                        options.Endpoint = endpoint!;
                        options.Protocol = OtlpExportProtocol.Grpc;
                    });
                }
            });

        return services;
    }

    private static bool IsNoiseEndpoint(PathString path) =>
        path.StartsWithSegments("/metrics") ||
        path.StartsWithSegments("/health/live");
}
