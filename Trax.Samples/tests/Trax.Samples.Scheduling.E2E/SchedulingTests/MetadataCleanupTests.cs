using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Samples.Scheduling.E2E.Fixtures;
using Trax.Scheduler.Trains.MetadataCleanup;

namespace Trax.Samples.Scheduling.E2E.SchedulingTests;

/// <summary>
/// <c>AddMetadataCleanup</c> deletes terminal runs of the trains it names once they are older
/// than their retention, and leaves every other train's runs alone.
/// </summary>
public class MetadataCleanupTests : SchedulingTestFixture
{
    [Test]
    public async Task Cleanup_deletes_a_listed_trains_run_past_its_retention()
    {
        var refresh = await Db.Manifest(ManifestNames.RefreshExchangeRates);
        var run = await Db.WaitForRun(
            refresh.Id,
            TrainState.Completed,
            afterId: 0,
            TimeSpan.FromSeconds(20)
        );

        await Backdate(run.Id, TimeSpan.FromDays(2)); // past the one-day retention
        await RunCleanup();

        (await Exists(run.Id)).Should().BeFalse();
    }

    [Test]
    public async Task Cleanup_keeps_a_listed_trains_run_inside_its_retention()
    {
        var refresh = await Db.Manifest(ManifestNames.RefreshExchangeRates);
        var run = await Db.WaitForRun(
            refresh.Id,
            TrainState.Completed,
            afterId: 0,
            TimeSpan.FromSeconds(20)
        );

        await Backdate(run.Id, TimeSpan.FromHours(2)); // past the 30-minute default, inside a day
        await RunCleanup();

        (await Exists(run.Id)).Should().BeTrue();
    }

    [Test]
    public async Task Cleanup_leaves_unlisted_trains_alone()
    {
        var launch = await Db.Manifest(ManifestNames.SendLaunchAnnouncement);
        var run = await Db.WaitForRun(
            launch.Id,
            TrainState.Completed,
            afterId: 0,
            TimeSpan.FromSeconds(40)
        );

        try
        {
            await Backdate(run.Id, TimeSpan.FromDays(30));
            await RunCleanup();

            (await Exists(run.Id)).Should().BeTrue("send-launch-announcement is not on the list");
        }
        finally
        {
            // ScheduleOnceTests reads this run's start time.
            await Db.Query(dc =>
                dc.Metadatas.Where(m => m.Id == run.Id)
                    .ExecuteUpdateAsync(s => s.SetProperty(m => m.StartTime, run.StartTime))
            );
        }
    }

    private static Task Backdate(long metadataId, TimeSpan age) =>
        Db.Query(dc =>
            dc.Metadatas.Where(m => m.Id == metadataId)
                .ExecuteUpdateAsync(s => s.SetProperty(m => m.StartTime, DateTime.UtcNow - age))
        );

    private static Task<bool> Exists(long metadataId) =>
        Db.Query(dc => dc.Metadatas.AnyAsync(m => m.Id == metadataId));

    /// <summary>
    /// Runs one sweep now rather than waiting for the polling service's next tick. The
    /// cleanup train is resolved from DI, as the polling service resolves it.
    /// </summary>
    private static async Task RunCleanup()
    {
        using var scope = Services.CreateScope();
        var cleanup = scope.ServiceProvider.GetRequiredService<IMetadataCleanupTrain>();
        await cleanup.Run(new MetadataCleanupRequest());
    }
}
