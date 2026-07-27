using System.Net;

using ConexaoSolidaria.ServiceDefaults.Health;

namespace ConexaoSolidaria.Tests;

public sealed class HttpDependencyReadinessProbeTests
{
    [Fact]
    public async Task CheckAsync_reports_healthy_when_every_dependency_succeeds()
    {
        using var client = new HttpClient(new StubHandler(request =>
            request.RequestUri!.AbsolutePath.Contains("gateway", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.OK)
                : new HttpResponseMessage(HttpStatusCode.NoContent)));

        var result = await HttpDependencyReadinessProbe.CheckAsync(
            client,
            Dependencies(),
            CancellationToken.None);

        Assert.True(result.IsHealthy);
        Assert.All(result.Dependencies.Values, dependency => Assert.Equal("Healthy", dependency.Status));
    }

    [Fact]
    public async Task CheckAsync_reports_unhealthy_status_code_without_hiding_other_checks()
    {
        using var client = new HttpClient(new StubHandler(request =>
            request.RequestUri!.AbsolutePath.Contains("gateway", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK)));

        var result = await HttpDependencyReadinessProbe.CheckAsync(
            client,
            Dependencies(),
            CancellationToken.None);

        Assert.False(result.IsHealthy);
        Assert.Equal(503, result.Dependencies["gateway"].StatusCode);
        Assert.Equal("Healthy", result.Dependencies["identity-provider"].Status);
    }

    [Fact]
    public async Task CheckAsync_reports_transport_failure_as_unhealthy()
    {
        using var client = new HttpClient(new StubHandler(_ => throw new HttpRequestException("offline")));

        var result = await HttpDependencyReadinessProbe.CheckAsync(
            client,
            new Dictionary<string, string> { ["gateway"] = "https://platform.example.test/gateway" },
            CancellationToken.None);

        Assert.False(result.IsHealthy);
        Assert.Equal("HttpRequestException", result.Dependencies["gateway"].Error);
    }

    private static IReadOnlyDictionary<string, string> Dependencies() => new Dictionary<string, string>
    {
        ["gateway"] = "https://platform.example.test/gateway",
        ["identity-provider"] = "https://platform.example.test/identity"
    };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responseFactory) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => Task.FromResult(responseFactory(request));
    }
}
