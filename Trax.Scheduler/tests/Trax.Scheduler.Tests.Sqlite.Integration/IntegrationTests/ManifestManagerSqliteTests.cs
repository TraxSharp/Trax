using System.Diagnostics;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.ManifestManagerPollingService;
using Trax.Scheduler.Tests.Sqlite.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Sqlite.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Sqlite.Integration.IntegrationTests;

/// <summary>
/// The manifest manager's real polling cycle on SQLite, which takes one writer at a time. The
/// cycle must not hold the write lock while its own train writes through other connections.
/// </summary>
[TestFixture]
public class ManifestManagerSqliteTests : TestSetup
{
    private static readonly TimeSpan Quick = TimeSpan.FromSeconds(5);

    [Test]
    public async Task Cycle_does_not_hold_the_write_lock()
    {
        var stale = await CreateStalePendingRun();
        var polling = new ManifestManagerPollingService(
            Scope.ServiceProvider,
            Scope.ServiceProvider.GetRequiredService<SchedulerConfiguration>(),
            NullLogger<ManifestManagerPollingService>.Instance,
            Scope.ServiceProvider.GetRequiredService<ISqlDialect>()
        );

        var cycleClock = Stopwatch.StartNew();
        var cycle = polling.RunManifestManager(CancellationToken.None);

        // A write from elsewhere in the host while the cycle runs.
        var writeClock = Stopwatch.StartNew();
        using (var other = Scope.ServiceProvider.CreateScope())
        {
            var context = other.ServiceProvider.GetRequiredService<IDataContext>();
            await context.Track(NewRun(TrainState.Completed, DateTime.UtcNow));
            await context.SaveChanges(CancellationToken.None);
        }
        writeClock.Stop();

        await cycle;
        cycleClock.Stop();

        writeClock.Elapsed.Should().BeLessThan(Quick, "the cycle must not hold the write lock");
        cycleClock.Elapsed.Should().BeLessThan(Quick, "the cycle's own train must not wait on it");

        DataContext.Reset();
        var run = await DataContext.Metadatas.AsNoTracking().SingleAsync(m => m.Id == stale.Id);
        run.TrainState.Should().Be(TrainState.Failed, "the stale run is reaped in that cycle");
    }

    private async Task<Metadata> CreateStalePendingRun()
    {
        var run = NewRun(TrainState.Pending, DateTime.UtcNow.AddHours(-1));
        await DataContext.Track(run);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return run;
    }

    private static Metadata NewRun(TrainState state, DateTime startTime)
    {
        var run = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(SchedulerTestTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = new SchedulerTestInput { Value = "stale" },
            }
        );
        run.TrainState = state;
        run.StartTime = startTime;
        return run;
    }
}
