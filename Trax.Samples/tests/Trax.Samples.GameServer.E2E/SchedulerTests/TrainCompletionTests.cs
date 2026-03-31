using LanguageExt;
using Trax.Effect.Enums;
using Trax.Samples.GameServer.E2E.Fixtures;
using Trax.Samples.GameServer.E2E.Utilities;
using Trax.Samples.GameServer.Trains.Leaderboard.RecalculateLeaderboard;
using Trax.Samples.GameServer.Trains.Matches.ProcessMatchResult;
using Trax.Samples.GameServer.Trains.Players.CleanupInactivePlayers;
using Trax.Samples.GameServer.Trains.Rewards.DistributeDailyRewards;

namespace Trax.Samples.GameServer.E2E.SchedulerTests;

[TestFixture]
public class TrainCompletionTests : SchedulerTestFixture
{
    [Test]
    public async Task RecalculateLeaderboard_Completes()
    {
        var output = await TrainBus.RunAsync<RecalculateLeaderboardOutput>(
            new RecalculateLeaderboardInput { Region = "global" }
        );

        output.Should().NotBeNull();
        output.Region.Should().Be("global");
        output.PlayersProcessed.Should().BeGreaterThan(0);
        output.TopPlayer.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task ProcessMatchResult_WithHighScoreDiff_ReturnsOutputWithAnomalies()
    {
        var output = await TrainBus.RunAsync<ProcessMatchResultOutput>(
            new ProcessMatchResultInput
            {
                Region = "na",
                MatchId = "e2e-match-001",
                WinnerId = "player-1",
                LoserId = "player-2",
                WinnerScore = 100,
                LoserScore = 10,
            }
        );

        output.Should().NotBeNull();
        output.MatchId.Should().Be("e2e-match-001");
        output.Region.Should().Be("na");

        // Score diff = 90, which is > 50, so anomalies should be detected
        output.AnomaliesDetected.Should().BeGreaterThan(0);
        output.CheatDetectionTriggered.Should().BeTrue();
    }

    [Test]
    public async Task ProcessMatchResult_WithLowScoreDiff_NoAnomalies()
    {
        var output = await TrainBus.RunAsync<ProcessMatchResultOutput>(
            new ProcessMatchResultInput
            {
                Region = "eu",
                MatchId = "e2e-match-002",
                WinnerId = "player-3",
                LoserId = "player-4",
                WinnerScore = 60,
                LoserScore = 55,
            }
        );

        output.Should().NotBeNull();
        output.AnomaliesDetected.Should().Be(0);
        output.CheatDetectionTriggered.Should().BeFalse();
    }

    [Test]
    public async Task CleanupInactivePlayers_Completes()
    {
        await TrainBus.RunAsync<Unit>(new CleanupInactivePlayersInput { InactiveDays = 90 });

        // If we got here without an exception, the train completed successfully.
        // Verify metadata was created.
        var metadata = await TrainStatePoller.WaitForMetadataByTrainName(
            DataContext,
            "CleanupInactivePlayers",
            TrainState.Completed,
            TimeSpan.FromSeconds(5)
        );

        metadata.Should().NotBeNull();
    }

    [Test]
    public async Task DistributeDailyRewards_Completes()
    {
        await TrainBus.RunAsync<Unit>(new DistributeDailyRewardsInput { RewardType = "E2ETest" });

        var metadata = await TrainStatePoller.WaitForMetadataByTrainName(
            DataContext,
            "DistributeDailyRewards",
            TrainState.Completed,
            TimeSpan.FromSeconds(5)
        );

        metadata.Should().NotBeNull();
    }
}
