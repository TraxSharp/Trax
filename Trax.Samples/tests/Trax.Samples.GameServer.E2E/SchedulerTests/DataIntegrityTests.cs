using Microsoft.EntityFrameworkCore;
using Trax.Effect.Enums;
using Trax.Samples.GameServer.E2E.Fixtures;
using Trax.Samples.GameServer.E2E.Utilities;
using Trax.Samples.GameServer.Trains.Leaderboard.RecalculateLeaderboard;
using Trax.Samples.GameServer.Trains.Maintenance.CorruptedDataRepair;

namespace Trax.Samples.GameServer.E2E.SchedulerTests;

/// <summary>
/// Tests that train execution correctly persists all metadata fields to the database:
/// input/output serialization, log entries, failure details, and timing data.
/// </summary>
[TestFixture]
public class DataIntegrityTests : SchedulerTestFixture
{
    #region InputOutput

    [Test]
    public async Task TrainCompletion_PersistsInputInMetadata()
    {
        await TrainBus.RunAsync<RecalculateLeaderboardOutput>(
            new RecalculateLeaderboardInput { Region = "data-integrity-test" }
        );

        var metadata = await TrainStatePoller.WaitForMetadataByTrainName(
            DataContext,
            "RecalculateLeaderboard",
            TrainState.Completed,
            TimeSpan.FromSeconds(5)
        );

        metadata.Input.Should().NotBeNullOrEmpty();
        metadata.Input.Should().Contain("data-integrity-test");
    }

    [Test]
    public async Task TrainCompletion_PersistsOutputInMetadata()
    {
        await TrainBus.RunAsync<RecalculateLeaderboardOutput>(
            new RecalculateLeaderboardInput { Region = "global" }
        );

        var metadata = await TrainStatePoller.WaitForMetadataByTrainName(
            DataContext,
            "RecalculateLeaderboard",
            TrainState.Completed,
            TimeSpan.FromSeconds(5)
        );

        metadata.Output.Should().NotBeNullOrEmpty();
        metadata.Output.Should().Contain("region");
        metadata.Output.Should().Contain("playersProcessed");
        metadata.Output.Should().Contain("topPlayer");
    }

    #endregion

    #region Logging

    [Test]
    public async Task TrainCompletion_CreatesLogEntries()
    {
        await TrainBus.RunAsync<RecalculateLeaderboardOutput>(
            new RecalculateLeaderboardInput { Region = "global" }
        );

        // DataContextLoggingProvider flushes logs asynchronously via a Channel
        // with a 1-second timer. Logs are application-level (not per-metadata).
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        List<Effect.Models.Log.Log> logs = [];
        while (DateTime.UtcNow < deadline)
        {
            DataContext.Reset();
            logs = await DataContext.Logs.AsNoTracking().ToListAsync();

            if (logs.Count > 0)
                break;

            await Task.Delay(250);
        }

        logs.Should()
            .NotBeEmpty(
                "AddDataContextLogging should persist application log entries to the database"
            );
    }

    #endregion

    #region FailureDetails

    [Test]
    public async Task TrainFailure_HasAllFailureFields()
    {
        try
        {
            await TrainBus.RunAsync<LanguageExt.Unit>(
                new CorruptedDataRepairInput { TableName = "player_sessions" }
            );
        }
        catch
        {
            // Expected to throw
        }

        var metadata = await TrainStatePoller.WaitForMetadataByTrainName(
            DataContext,
            "CorruptedDataRepair",
            TrainState.Failed,
            TimeSpan.FromSeconds(5)
        );

        metadata
            .FailureReason.Should()
            .NotBeNullOrEmpty("failed trains should record the failure reason");
        metadata
            .FailureJunction.Should()
            .NotBeNullOrEmpty("failed trains should record which junction failed");
        metadata
            .FailureException.Should()
            .NotBeNullOrEmpty("failed trains should record the exception type");
    }

    #endregion

    #region Timing

    [Test]
    public async Task TrainCompletion_HasTimingData()
    {
        await TrainBus.RunAsync<RecalculateLeaderboardOutput>(
            new RecalculateLeaderboardInput { Region = "global" }
        );

        var metadata = await TrainStatePoller.WaitForMetadataByTrainName(
            DataContext,
            "RecalculateLeaderboard",
            TrainState.Completed,
            TimeSpan.FromSeconds(5)
        );

        metadata.StartTime.Should().NotBe(default, "completed trains should have a start time");
        metadata.EndTime.Should().NotBeNull("completed trains should have an end time");
        metadata
            .EndTime.Should()
            .BeAfter(metadata.StartTime, "end time should be after start time");
    }

    [Test]
    public async Task TrainFailure_HasTimingData()
    {
        try
        {
            await TrainBus.RunAsync<LanguageExt.Unit>(
                new CorruptedDataRepairInput { TableName = "player_sessions" }
            );
        }
        catch
        {
            // Expected to throw
        }

        var metadata = await TrainStatePoller.WaitForMetadataByTrainName(
            DataContext,
            "CorruptedDataRepair",
            TrainState.Failed,
            TimeSpan.FromSeconds(5)
        );

        metadata.StartTime.Should().NotBe(default, "failed trains should still have a start time");
        metadata.EndTime.Should().NotBeNull("failed trains should still have an end time");
    }

    #endregion
}
