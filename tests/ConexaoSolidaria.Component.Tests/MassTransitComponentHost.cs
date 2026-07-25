using MassTransit;
using MassTransit.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace ConexaoSolidaria.Component.Tests;

internal sealed class MassTransitComponentHost(
    SqliteConnection connection,
    ServiceProvider services,
    ITestHarness harness) : IAsyncDisposable
{
    public IServiceProvider Services => services;
    public ITestHarness Harness => harness;

    public static async Task<MassTransitComponentHost> CreateAsync(
        Action<IServiceCollection, SqliteConnection> configureServices,
        Action<IBusRegistrationConfigurator> configureBus)
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        configureServices(services, connection);
        services.AddMassTransitTestHarness(configureBus);

        var provider = services.BuildServiceProvider(validateScopes: true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        return new MassTransitComponentHost(connection, provider, harness);
    }

    public async ValueTask DisposeAsync()
    {
        await harness.Stop();
        await services.DisposeAsync();
        await connection.DisposeAsync();
    }
}
