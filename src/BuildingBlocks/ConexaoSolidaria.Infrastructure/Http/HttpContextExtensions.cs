using ConexaoSolidaria.Contracts.Auth;
using Microsoft.AspNetCore.Http;

namespace ConexaoSolidaria.Infrastructure.Http;

public static class HttpContextExtensions
{
    public static string TenantId(this HttpContext context)
    {
        return context.Items.TryGetValue(CorrelationTenantMiddleware.TenantHeader, out var value)
            ? value?.ToString() ?? AuthDefaults.DefaultTenantId
            : AuthDefaults.DefaultTenantId;
    }

    public static string CorrelationId(this HttpContext context)
    {
        return context.Items.TryGetValue(CorrelationTenantMiddleware.CorrelationHeader, out var value)
            ? value?.ToString() ?? Guid.NewGuid().ToString("N")
            : Guid.NewGuid().ToString("N");
    }
}
