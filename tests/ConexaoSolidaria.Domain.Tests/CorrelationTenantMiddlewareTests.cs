using System.Security.Claims;

using ConexaoSolidaria.Contracts.Auth;
using ConexaoSolidaria.ServiceDefaults.Http;

using Microsoft.AspNetCore.Http;

namespace ConexaoSolidaria.Tests;

public sealed class CorrelationTenantMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_prioritizes_authenticated_tenant_over_untrusted_input()
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(AuthDefaults.TenantClaim, "esperanca-solidaria")],
            "test"));
        context.Request.Headers[CorrelationTenantMiddleware.TenantHeader] = "mare-limpa";
        context.Request.QueryString = new QueryString("?tenant=futuro-em-rede");
        var middleware = new CorrelationTenantMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.Equal("esperanca-solidaria", context.Items[CorrelationTenantMiddleware.TenantHeader]);
        Assert.Equal("esperanca-solidaria", context.Response.Headers[CorrelationTenantMiddleware.TenantHeader]);
    }

    [Fact]
    public async Task InvokeAsync_resolves_public_tenant_selection_and_preserves_correlation()
    {
        var context = new DefaultHttpContext();
        context.Request.QueryString = new QueryString("?tenant=mare-limpa");
        context.Request.Headers[CorrelationTenantMiddleware.CorrelationHeader] = "journey-123";
        var middleware = new CorrelationTenantMiddleware(_ => Task.CompletedTask);

        await middleware.InvokeAsync(context);

        Assert.Equal("mare-limpa", context.Items[CorrelationTenantMiddleware.TenantHeader]);
        Assert.Equal("journey-123", context.Items[CorrelationTenantMiddleware.CorrelationHeader]);
        Assert.Equal("journey-123", context.Response.Headers[CorrelationTenantMiddleware.CorrelationHeader]);
    }
}
