using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Samples.EnergyHub.E2E.Factories;
using Trax.Scheduler.Configuration;

namespace Trax.Samples.EnergyHub.E2E.HubTests;

[SetUpFixture]
public class SharedHubSetup
{
    public static EnergyHubFactory Factory { get; private set; } = null!;

    public static EnergyWorkerFactory Worker { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        Factory = new EnergyHubFactory();
        _ = Factory.Services;

        // The hub migrates the database and seeds the manifests; start the worker after it.
        Worker = new EnergyWorkerFactory();
        _ = Worker.Services;

        await WaitForManifestsSeeded();

        var config = Factory.Services.GetRequiredService<SchedulerConfiguration>();
        config.ManifestManagerEnabled = false;
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        // The broker may close a connection first during shutdown; that is safe to ignore.
        try
        {
            await Worker.DisposeAsync();
        }
        catch (RabbitMQ.Client.Exceptions.AlreadyClosedException) { }

        try
        {
            await Factory.DisposeAsync();
        }
        catch (RabbitMQ.Client.Exceptions.AlreadyClosedException) { }

        Npgsql.NpgsqlConnection.ClearAllPools();
    }

    private async Task WaitForManifestsSeeded()
    {
        using var scope = Factory.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var dc = (IDataContext)factory.Create();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (DateTime.UtcNow < deadline)
        {
            dc.Reset();
            var count = await dc.Manifests.AsNoTracking().CountAsync();
            if (count > 0)
                return;
            await Task.Delay(250);
        }

        throw new TimeoutException("Manifests were not seeded within 15 seconds.");
    }
}
