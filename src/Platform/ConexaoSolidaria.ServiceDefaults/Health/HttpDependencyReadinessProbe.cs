namespace ConexaoSolidaria.ServiceDefaults.Health;

public sealed record HttpDependencyStatus(string Status, int? StatusCode = null, string? Error = null);

public sealed record HttpDependencyReadinessResult(
    bool IsHealthy,
    IReadOnlyDictionary<string, HttpDependencyStatus> Dependencies);

public static class HttpDependencyReadinessProbe
{
    public static async Task<HttpDependencyReadinessResult> CheckAsync(
        HttpClient client,
        IReadOnlyDictionary<string, string> dependencies,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(dependencies);

        var checks = new Dictionary<string, HttpDependencyStatus>(StringComparer.OrdinalIgnoreCase);
        var healthy = true;

        foreach (var dependency in dependencies)
        {
            try
            {
                using var response = await client.GetAsync(dependency.Value, cancellationToken);
                var available = response.IsSuccessStatusCode;
                healthy &= available;
                checks[dependency.Key] = new HttpDependencyStatus(
                    available ? "Healthy" : "Unhealthy",
                    (int)response.StatusCode);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                healthy = false;
                checks[dependency.Key] = new HttpDependencyStatus(
                    "Unhealthy",
                    Error: exception.GetType().Name);
            }
        }

        return new HttpDependencyReadinessResult(healthy, checks);
    }
}
