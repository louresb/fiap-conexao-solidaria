using ConexaoSolidaria.ServiceDefaults.Http;

using Microsoft.AspNetCore.Http;

namespace ConexaoSolidaria.Tests;

public sealed class SecurityHeadersMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_applies_the_platform_security_baseline()
    {
        var context = new DefaultHttpContext();
        var nextCalled = false;
        var middleware = new SecurityHeadersMiddleware(_ =>
        {
            nextCalled = true;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        Assert.Equal("nosniff", context.Response.Headers["X-Content-Type-Options"]);
        Assert.Equal("DENY", context.Response.Headers["X-Frame-Options"]);
        Assert.Equal("strict-origin-when-cross-origin", context.Response.Headers["Referrer-Policy"]);
        Assert.Equal("camera=(), geolocation=(), microphone=()", context.Response.Headers["Permissions-Policy"]);
        Assert.Equal("none", context.Response.Headers["X-Permitted-Cross-Domain-Policies"]);
    }
}
