using ConexaoSolidaria.ServiceDefaults.Http;

using Microsoft.AspNetCore.HttpOverrides;

namespace ConexaoSolidaria.Tests;

public sealed class ForwardedHeadersConfigurationTests
{
    [Fact]
    public void CreateOptions_keeps_default_trusted_proxies_when_dynamic_proxy_is_disabled()
    {
        var options = ApplicationBuilderExtensions.CreateForwardedHeadersOptions(false);

        Assert.Equal(1, options.ForwardLimit);
        Assert.NotEmpty(options.KnownIPNetworks);
        Assert.NotEmpty(options.KnownProxies);
    }

    [Fact]
    public void CreateOptions_trusts_one_dynamic_proxy_hop_when_explicitly_enabled()
    {
        var options = ApplicationBuilderExtensions.CreateForwardedHeadersOptions(true);

        Assert.Equal(1, options.ForwardLimit);
        Assert.Empty(options.KnownIPNetworks);
        Assert.Empty(options.KnownProxies);
        Assert.Equal(
            ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
            options.ForwardedHeaders);
    }
}
