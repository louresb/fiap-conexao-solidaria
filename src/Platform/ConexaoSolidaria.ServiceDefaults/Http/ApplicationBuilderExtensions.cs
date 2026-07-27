using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Configuration;

namespace ConexaoSolidaria.ServiceDefaults.Http;

public static class ApplicationBuilderExtensions
{
    public static IApplicationBuilder UseConfiguredForwardedHeaders(
        this IApplicationBuilder app,
        IConfiguration configuration)
    {
        var options = CreateForwardedHeadersOptions(
            configuration.GetValue("Proxy:TrustForwardedHeaders", false));

        return app.UseForwardedHeaders(options);
    }

    public static ForwardedHeadersOptions CreateForwardedHeadersOptions(bool trustDynamicProxy)
    {
        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            ForwardLimit = 1
        };

        if (trustDynamicProxy)
        {
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        }

        return options;
    }

    public static IApplicationBuilder UseCorrelationAndTenant(this IApplicationBuilder app)
    {
        return app.UseMiddleware<CorrelationTenantMiddleware>();
    }
}
