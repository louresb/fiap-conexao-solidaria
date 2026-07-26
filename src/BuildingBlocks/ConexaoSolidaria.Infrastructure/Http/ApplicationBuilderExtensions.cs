using Microsoft.AspNetCore.Builder;

namespace ConexaoSolidaria.Infrastructure.Http;

public static class ApplicationBuilderExtensions
{
    public static IApplicationBuilder UseCorrelationAndTenant(this IApplicationBuilder app)
    {
        return app.UseMiddleware<CorrelationTenantMiddleware>();
    }
}
