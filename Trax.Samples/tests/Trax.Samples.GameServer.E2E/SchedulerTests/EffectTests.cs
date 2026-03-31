using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Enums;
using Trax.Effect.Utils;
using Trax.Samples.GameServer.E2E.Fixtures;
using Trax.Samples.GameServer.E2E.Utilities;
using Trax.Samples.GameServer.Trains.Leaderboard.RecalculateLeaderboard;
using Trax.Samples.GameServer.Trains.Maintenance.CorruptedDataRepair;

namespace Trax.Samples.GameServer.E2E.SchedulerTests;

/// <summary>
/// Tests that every effect configured in the scheduler's AddEffects() pipeline
/// actually works end-to-end: parameter serialization round-trips, junction progress
/// tracking, lifecycle hooks, data context logging, and JSON change detection.
/// </summary>
[TestFixture]
public class EffectTests : SchedulerTestFixture
{
    #region SaveTrainParameters — Round-Trip Deserialization

    [Test]
    public async Task SaveTrainParameters_Input_DeserializesBackToOriginalType()
    {
        var originalInput = new RecalculateLeaderboardInput { Region = "round-trip-test" };

        await TrainBus.RunAsync<RecalculateLeaderboardOutput>(originalInput);

        var metadata = await TrainStatePoller.WaitForMetadataByTrainName(
            DataContext,
            "RecalculateLeaderboard",
            TrainState.Completed,
            TimeSpan.FromSeconds(5)
        );

        metadata.Input.Should().NotBeNullOrEmpty();

        // Deserialize the stored JSON back to the original type — proves the serialization
        // format is correct, not just that "something" was stored.
        var deserialized = JsonSerializer.Deserialize<RecalculateLeaderboardInput>(
            metadata.Input!,
            TraxJsonSerializationOptions.Default
        );

        deserialized.Should().NotBeNull();
        deserialized!.Region.Should().Be("round-trip-test");
    }

    [Test]
    public async Task SaveTrainParameters_Output_DeserializesBackToOriginalType()
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

        var deserialized = JsonSerializer.Deserialize<RecalculateLeaderboardOutput>(
            metadata.Output!,
            TraxJsonSerializationOptions.Default
        );

        deserialized.Should().NotBeNull();
        deserialized!.Region.Should().Be("global");
        deserialized.PlayersProcessed.Should().BeGreaterThan(0);
        deserialized.TopPlayer.Should().NotBeNullOrEmpty();
        deserialized.TopRating.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task SaveTrainParameters_FailedTrain_StillPersistsInput()
    {
        try
        {
            await TrainBus.RunAsync<LanguageExt.Unit>(
                new CorruptedDataRepairInput { TableName = "effect_test_table" }
            );
        }
        catch
        {
            // Expected
        }

        var metadata = await TrainStatePoller.WaitForMetadataByTrainName(
            DataContext,
            "CorruptedDataRepair",
            TrainState.Failed,
            TimeSpan.FromSeconds(5)
        );

        metadata.Input.Should().NotBeNullOrEmpty();

        var deserialized = JsonSerializer.Deserialize<CorruptedDataRepairInput>(
            metadata.Input!,
            TraxJsonSerializationOptions.Default
        );

        deserialized.Should().NotBeNull();
        deserialized!.TableName.Should().Be("effect_test_table");
    }

    #endregion

    #region AddJunctionProgress — CurrentlyRunningJunction Tracking

    [Test]
    public async Task JunctionProgress_AfterCompletion_CurrentlyRunningJunctionIsCleared()
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

        // After completion, JunctionProgressProvider clears CurrentlyRunningJunction.
        // If AddJunctionProgress is wired correctly, these should be null.
        metadata
            .CurrentlyRunningJunction.Should()
            .BeNull(
                "AddJunctionProgress should clear CurrentlyRunningJunction after the last junction completes"
            );
        metadata
            .JunctionStartedAt.Should()
            .BeNull(
                "AddJunctionProgress should clear JunctionStartedAt after the last junction completes"
            );
    }

    [Test]
    public async Task JunctionProgress_FailedTrain_RecordsFailureJunction()
    {
        try
        {
            await TrainBus.RunAsync<LanguageExt.Unit>(
                new CorruptedDataRepairInput { TableName = "junction_progress_test" }
            );
        }
        catch
        {
            // Expected
        }

        var metadata = await TrainStatePoller.WaitForMetadataByTrainName(
            DataContext,
            "CorruptedDataRepair",
            TrainState.Failed,
            TimeSpan.FromSeconds(5)
        );

        // Failed trains should record which junction failed — this proves
        // the junction progress tracking was active during execution.
        metadata
            .FailureJunction.Should()
            .NotBeNullOrEmpty("AddJunctionProgress should enable junction-level failure tracking");
    }

    #endregion

    #region AddLifecycleHook<AuditLogHook> — Hook Execution

    [Test]
    public async Task AuditLogHook_OnCompleted_LogsAuditEntry()
    {
        await TrainBus.RunAsync<RecalculateLeaderboardOutput>(
            new RecalculateLeaderboardInput { Region = "audit-hook-test" }
        );

        // AuditLogHook logs via ILogger<AuditLogHook> with "[AUDIT]" prefix.
        // DataContextLogging captures this to the trax.log table.
        // Wait for async log flush (1s Channel timer).
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        List<Effect.Models.Log.Log> auditLogs = [];
        while (DateTime.UtcNow < deadline)
        {
            DataContext.Reset();
            auditLogs = await DataContext
                .Logs.AsNoTracking()
                .Where(l => l.Message.Contains("[AUDIT]"))
                .Where(l => l.Message.Contains("completed"))
                .ToListAsync();

            if (auditLogs.Count > 0)
                break;

            await Task.Delay(250);
        }

        auditLogs
            .Should()
            .NotBeEmpty(
                "AuditLogHook should log [AUDIT] completion messages that DataContextLogging persists"
            );

        var auditLog = auditLogs.First();
        auditLog.Category.Should().Contain("AuditLogHook");
    }

    [Test]
    public async Task AuditLogHook_OnFailed_LogsFailureAuditEntry()
    {
        try
        {
            await TrainBus.RunAsync<LanguageExt.Unit>(
                new CorruptedDataRepairInput { TableName = "audit-failure-test" }
            );
        }
        catch
        {
            // Expected
        }

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        List<Effect.Models.Log.Log> auditLogs = [];
        while (DateTime.UtcNow < deadline)
        {
            DataContext.Reset();
            auditLogs = await DataContext
                .Logs.AsNoTracking()
                .Where(l => l.Message.Contains("[AUDIT]"))
                .Where(l => l.Message.Contains("failed"))
                .ToListAsync();

            if (auditLogs.Count > 0)
                break;

            await Task.Delay(250);
        }

        auditLogs
            .Should()
            .NotBeEmpty(
                "AuditLogHook should log [AUDIT] failure messages that DataContextLogging persists"
            );

        var auditLog = auditLogs.First();
        auditLog.Category.Should().Contain("AuditLogHook");
    }

    #endregion

    #region AddDataContextLogging — Log Content Quality

    [Test]
    public async Task DataContextLogging_PersistsLogCategory()
    {
        await TrainBus.RunAsync<RecalculateLeaderboardOutput>(
            new RecalculateLeaderboardInput { Region = "global" }
        );

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

        logs.Should().NotBeEmpty();

        // Verify logs have meaningful content — not just empty stubs.
        logs.Should()
            .AllSatisfy(log =>
            {
                log.Category.Should().NotBeNullOrEmpty("every log should have a category");
                log.Message.Should().NotBeNullOrEmpty("every log should have a message");
            });
    }

    #endregion
}
