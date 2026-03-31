using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Samples.GameServer.E2E.Factories;
using Trax.Scheduler.Configuration;

namespace Trax.Samples.GameServer.E2E.SchedulerTests;

/// <summary>
/// Shares a single GameServerSchedulerFactory across ALL scheduler test fixtures.
/// NUnit creates one factory at namespace setup time and disposes it after all
/// scheduler tests complete — preventing connection pool exhaustion from 12+
/// separate WebApplicationFactory instances.
/// </summary>
[SetUpFixture]
public class SharedSchedulerSetup
{
    public static GameServerSchedulerFactory Factory { get; private set; } = null!;

    [OneTimeSetUp]
    public async Task OneTimeSetUp()
    {
        Factory = new GameServerSchedulerFactory();

        // Accessing Services triggers host startup (seeds manifests, recovers stuck jobs).
        _ = Factory.Services;

        await WaitForManifestsSeeded();

        // Disable ManifestManager after seeding to prevent automatic scheduling interference.
        var config = Factory.Services.GetRequiredService<SchedulerConfiguration>();
        config.ManifestManagerEnabled = false;
    }

    [OneTimeTearDown]
    public async Task OneTimeTearDown()
    {
        await Factory.DisposeAsync();
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
