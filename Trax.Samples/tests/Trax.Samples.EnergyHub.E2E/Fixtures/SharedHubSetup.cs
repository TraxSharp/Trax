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

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        Factory = new EnergyHubFactory();
        _ = Factory.Services;

        await WaitForManifestsSeeded();

        var config = Factory.Services.GetRequiredService<SchedulerConfiguration>();
        config.ManifestManagerEnabled = false;
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        try
        {
            await Factory.DisposeAsync();
        }
        catch (RabbitMQ.Client.Exceptions.AlreadyClosedException)
        {
            // RabbitMQ connection may already be closed during shutdown — safe to ignore.
        }

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
