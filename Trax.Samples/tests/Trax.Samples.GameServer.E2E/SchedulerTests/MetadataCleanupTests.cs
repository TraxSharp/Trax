using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Samples.GameServer.E2E.Fixtures;
using Trax.Samples.GameServer.E2E.Utilities;
using Trax.Samples.GameServer.Trains.Leaderboard.RecalculateLeaderboard;
using Trax.Scheduler.Trains.MetadataCleanup;

namespace Trax.Samples.GameServer.E2E.SchedulerTests;

/// <summary>
/// Tests that AddMetadataCleanup correctly purges expired terminal-state metadata
/// for configured train types while preserving recent or non-terminal entries.
/// </summary>
[TestFixture]
public class MetadataCleanupTests : SchedulerTestFixture
{
    [Test]
    public async Task MetadataCleanup_DeletesExpiredTerminalMetadata()
    {
        // Run a train to create metadata.
        await TrainBus.RunAsync<RecalculateLeaderboardOutput>(
            new RecalculateLeaderboardInput { Region = "cleanup-test" }
        );

        var metadata = await TrainStatePoller.WaitForMetadataByTrainName(
            DataContext,
            "RecalculateLeaderboard",
            TrainState.Completed,
            TimeSpan.FromSeconds(5)
        );

        var metadataId = metadata.Id;

        // Backdate the metadata so it's older than the retention period.
        DataContext.Reset();
        var tracked = await DataContext.Metadatas.FirstAsync(m => m.Id == metadataId);
        tracked.StartTime = DateTime.UtcNow.AddHours(-2);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Set a very short retention period so the cleanup considers it expired.
        var config = GetSchedulerConfiguration();
        var originalRetention = config.MetadataCleanup!.RetentionPeriod;
        config.MetadataCleanup!.RetentionPeriod = TimeSpan.FromSeconds(1);

        try
        {
            // Invoke the cleanup train directly via DI (it's not in the mediator
            // registry — the polling service also resolves it directly).
            using var cleanupScope = SharedSchedulerSetup.Factory.Services.CreateScope();
            var cleanupTrain =
                cleanupScope.ServiceProvider.GetRequiredService<IMetadataCleanupTrain>();
            await cleanupTrain.Run(new MetadataCleanupRequest());

            DataContext.Reset();
            var exists = await DataContext
                .Metadatas.AsNoTracking()
                .AnyAsync(m => m.Id == metadataId);

            exists
                .Should()
                .BeFalse("expired terminal metadata should be deleted by the cleanup train");
        }
        finally
        {
            config.MetadataCleanup!.RetentionPeriod = originalRetention;
        }
    }

    [Test]
    public async Task MetadataCleanup_PreservesRecentMetadata()
    {
        // Run a train to create fresh metadata.
        await TrainBus.RunAsync<RecalculateLeaderboardOutput>(
            new RecalculateLeaderboardInput { Region = "preserve-test" }
        );

        var metadata = await TrainStatePoller.WaitForMetadataByTrainName(
            DataContext,
            "RecalculateLeaderboard",
            TrainState.Completed,
            TimeSpan.FromSeconds(5)
        );

        var metadataId = metadata.Id;

        // Invoke cleanup directly — fresh metadata should be preserved.
        using var cleanupScope = SharedSchedulerSetup.Factory.Services.CreateScope();
        var cleanupTrain = cleanupScope.ServiceProvider.GetRequiredService<IMetadataCleanupTrain>();
        await cleanupTrain.Run(new MetadataCleanupRequest());

        DataContext.Reset();
        var exists = await DataContext.Metadatas.AsNoTracking().AnyAsync(m => m.Id == metadataId);

        exists.Should().BeTrue("metadata within the retention period should not be cleaned up");
    }
}
