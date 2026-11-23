using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Samples.EnergyHub.E2E.Factories;
using Trax.Scheduler.Configuration;

namespace Trax.Samples.EnergyHub.E2E.HubTests;

[SetUpFixture]
public class SharedHubSetup
{
    public static EnergyHubFactory Factory
    {
        get => field.NotNull(nameof(Factory));
        private set;
    }

    public static EnergyWorkerFactory Worker
    {
        get => field.NotNull(nameof(Worker));
        private set;
    }

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
        var scope = Factory.Services.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var factory = scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        var dc = (IDataContext)factory.Create();

        var deadline = TimeProvider.System.GetUtcNow().UtcDateTime + TimeSpan.FromSeconds(15);
        while (TimeProvider.System.GetUtcNow().UtcDateTime < deadline)
        {
            dc.Reset();
            var count = await dc.Manifests.AsNoTracking().CountAsync().ConfigureAwait(false);
            if (count > 0)
                return;
            await Task.Delay(250).ConfigureAwait(false);
        }

        throw new TimeoutException("Manifests were not seeded within 15 seconds.");
    }
}
