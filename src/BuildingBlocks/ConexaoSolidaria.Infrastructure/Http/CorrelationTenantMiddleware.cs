using System.Security.Claims;
using ConexaoSolidaria.Contracts.Auth;
using Microsoft.AspNetCore.Http;
using Serilog.Context;

namespace ConexaoSolidaria.Infrastructure.Http;

public sealed class CorrelationTenantMiddleware
{
    public const string CorrelationHeader = "X-Correlation-Id";
    public const string TenantHeader = "X-Tenant-Id";

    private readonly RequestDelegate _next;

    public CorrelationTenantMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveHeader(context, CorrelationHeader) ?? Guid.NewGuid().ToString("N");
        var tenantId = ResolveTenant(context);

        context.Response.Headers[CorrelationHeader] = correlationId;
        context.Response.Headers[TenantHeader] = tenantId;
        context.Items[CorrelationHeader] = correlationId;
        context.Items[TenantHeader] = tenantId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        using (LogContext.PushProperty("TenantId", tenantId))
        {
            await _next(context);
        }
    }

    private static string? ResolveHeader(HttpContext context, string header)
    {
        return context.Request.Headers.TryGetValue(header, out var values) && values.Count > 0
            ? values[0]
            : null;
    }

    private static string ResolveTenant(HttpContext context)
    {
        var claimTenant = context.User.FindFirstValue(AuthDefaults.TenantClaim);
        return claimTenant
            ?? ResolveHeader(context, TenantHeader)
            ?? context.Request.Query["tenantId"].FirstOrDefault()
            ?? AuthDefaults.DefaultTenantId;
    }
}
