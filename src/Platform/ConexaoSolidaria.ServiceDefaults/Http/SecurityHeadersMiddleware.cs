using Microsoft.AspNetCore.Http;

namespace ConexaoSolidaria.ServiceDefaults.Http;

public sealed class SecurityHeadersMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        context.Response.Headers["Permissions-Policy"] = "camera=(), geolocation=(), microphone=()";
        context.Response.Headers["X-Permitted-Cross-Domain-Policies"] = "none";

        await next(context);
    }
}
