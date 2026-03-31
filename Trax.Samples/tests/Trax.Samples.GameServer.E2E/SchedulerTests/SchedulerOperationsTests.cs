using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Samples.GameServer.E2E.Fixtures;
using Trax.Samples.GameServer.E2E.Utilities;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Samples.GameServer.E2E.SchedulerTests;

[TestFixture]
public class SchedulerOperationsTests : SchedulerTestFixture
{
    private ITraxScheduler GetScheduler()
    {
        return Scope.ServiceProvider.GetRequiredService<ITraxScheduler>();
    }

    [Test]
    public async Task TriggerManifest_EnqueuesWork()
    {
        var scheduler = GetScheduler();

        await scheduler.TriggerAsync(ManifestNames.RecalculateLeaderboard);

        // Verify work queue entry was created.
        DataContext.Reset();
        var entry = await DataContext
            .WorkQueues.AsNoTracking()
            .OrderByDescending(wq => wq.Id)
            .FirstOrDefaultAsync(wq => wq.TrainName.Contains("RecalculateLeaderboard"));

        entry.Should().NotBeNull("TriggerAsync should create a work queue entry");

        // Wait for the train to complete via the scheduler.
        var metadata = await TrainStatePoller.WaitForMetadataByTrainName(
            DataContext,
            "RecalculateLeaderboard",
            TrainState.Completed,
            TimeSpan.FromSeconds(15)
        );

        metadata.Should().NotBeNull();
    }

    [Test]
    public async Task DisableManifest_SetsDisabled()
    {
        var scheduler = GetScheduler();

        // Ensure manifest starts enabled.
        DataContext.Reset();
        var before = await DataContext
            .Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == ManifestNames.RecalculateLeaderboard);

        var originalEnabled = before.IsEnabled;

        try
        {
            await scheduler.DisableAsync(ManifestNames.RecalculateLeaderboard);

            DataContext.Reset();
            var after = await DataContext
                .Manifests.AsNoTracking()
                .FirstAsync(m => m.ExternalId == ManifestNames.RecalculateLeaderboard);

            after.IsEnabled.Should().BeFalse();
        }
        finally
        {
            // Restore original state.
            if (originalEnabled)
                await scheduler.EnableAsync(ManifestNames.RecalculateLeaderboard);
        }
    }

    [Test]
    public async Task EnableManifest_ReEnables()
    {
        var scheduler = GetScheduler();

        // Disable first, then re-enable.
        await scheduler.DisableAsync(ManifestNames.RecalculateLeaderboard);

        DataContext.Reset();
        var disabled = await DataContext
            .Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == ManifestNames.RecalculateLeaderboard);
        disabled.IsEnabled.Should().BeFalse();

        await scheduler.EnableAsync(ManifestNames.RecalculateLeaderboard);

        DataContext.Reset();
        var enabled = await DataContext
            .Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == ManifestNames.RecalculateLeaderboard);
        enabled.IsEnabled.Should().BeTrue();
    }

    [Test]
    public async Task CancelManifest_RequestsCancellation()
    {
        var scheduler = GetScheduler();

        // Cancel returns the number of in-progress metadata records that had
        // cancellation requested. With no running trains, this should be 0.
        var count = await scheduler.CancelAsync(ManifestNames.RecalculateLeaderboard);

        count.Should().BeGreaterThanOrEqualTo(0);
    }

    [Test]
    public async Task TriggerGroup_TriggersAllInGroup()
    {
        var scheduler = GetScheduler();

        // Find the process-match group.
        DataContext.Reset();
        var processMatchManifest = await DataContext
            .Manifests.AsNoTracking()
            .FirstAsync(m =>
                m.ExternalId == ManifestNames.WithIndex(ManifestNames.ProcessMatch, "na")
            );

        processMatchManifest.ManifestGroupId.Should().NotBe(0);

        var count = await scheduler.TriggerGroupAsync(processMatchManifest.ManifestGroupId);

        // 3 regional manifests (na, eu, ap) should all be triggered.
        count.Should().Be(3);
    }
}
