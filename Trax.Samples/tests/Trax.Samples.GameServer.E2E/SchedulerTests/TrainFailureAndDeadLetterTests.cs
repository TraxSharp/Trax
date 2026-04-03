using Microsoft.EntityFrameworkCore;
using Trax.Core.Exceptions;
using Trax.Effect.Enums;
using Trax.Samples.GameServer.E2E.Fixtures;
using Trax.Samples.GameServer.E2E.Utilities;
using Trax.Samples.GameServer.Trains.Maintenance.CorruptedDataRepair;

namespace Trax.Samples.GameServer.E2E.SchedulerTests;

[TestFixture]
public class TrainFailureAndDeadLetterTests : SchedulerTestFixture
{
    [Test]
    public async Task CorruptedDataRepair_FailsWithExpectedException()
    {
        var act = () =>
            TrainBus.RunAsync<LanguageExt.Unit>(
                new CorruptedDataRepairInput { TableName = "player_sessions" }
            );

        await act.Should().ThrowAsync<Exception>().WithMessage("*data corruption too severe*");
    }

    [Test]
    public async Task CorruptedDataRepair_FailedMetadata_HasFailureDetails()
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

        metadata.FailureReason.Should().Contain("data corruption too severe");
        metadata.FailureJunction.Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task CorruptedDataRepair_SchedulerFired_EventuallyDeadLetters()
    {
        // Re-enable ManifestManager so the scheduler auto-fires corrupted-data-repair,
        // retries it, and eventually dead-letters it.
        EnableManifestManager();

        try
        {
            var manifest = await DataContext
                .Manifests.AsNoTracking()
                .FirstAsync(m => m.ExternalId == ManifestNames.CorruptedDataRepair);

            // The scheduler fires this train every 30s with MaxRetries(1).
            // With reduced retry delay (2s), after the initial attempt + 1 retry,
            // it should dead-letter relatively quickly.
            await TrainStatePoller.WaitForDeadLetter(
                DataContext,
                manifest.Id,
                TimeSpan.FromSeconds(60)
            );

            var deadLetter = await DataContext
                .DeadLetters.AsNoTracking()
                .FirstAsync(dl => dl.ManifestId == manifest.Id);

            deadLetter.Should().NotBeNull();
        }
        finally
        {
            DisableManifestManager();
        }
    }

    #region Exception Persistence E2E (Real Postgres)

    [Test]
    public async Task CorruptedDataRepair_FailureException_IsOriginalType()
    {
        try
        {
            await TrainBus.RunAsync<LanguageExt.Unit>(
                new CorruptedDataRepairInput { TableName = "player_sessions" }
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

        metadata.FailureException.Should().Be("InvalidOperationException");
    }

    [Test]
    public async Task CorruptedDataRepair_FailureJunction_IdentifiesCorrectJunction()
    {
        try
        {
            await TrainBus.RunAsync<LanguageExt.Unit>(
                new CorruptedDataRepairInput { TableName = "player_sessions" }
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

        metadata.FailureJunction.Should().Be("AttemptRepairJunction");
    }

    [Test]
    public async Task CorruptedDataRepair_StackTrace_ContainsOriginalThrowSite()
    {
        try
        {
            await TrainBus.RunAsync<LanguageExt.Unit>(
                new CorruptedDataRepairInput { TableName = "player_sessions" }
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

        metadata.StackTrace.Should().NotBeNullOrEmpty();
        metadata.StackTrace.Should().Contain("AttemptRepairJunction");
    }

    [Test]
    public async Task CorruptedDataRepair_FailureReason_IsHumanReadable()
    {
        try
        {
            await TrainBus.RunAsync<LanguageExt.Unit>(
                new CorruptedDataRepairInput { TableName = "player_sessions" }
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

        // Must be the original human-readable message, not a JSON blob
        metadata.FailureReason.Should().NotStartWith("{");
        metadata.FailureReason.Should().Contain("data corruption too severe");
        metadata.FailureReason.Should().Contain("player_sessions");
    }

    [Test]
    public async Task CorruptedDataRepair_ExceptionThrownToCaller_HasOriginalMessage()
    {
        // The exception thrown to the caller must be the original type with
        // the original message — not a TrainException wrapping JSON.
        Exception? caught = null;
        try
        {
            await TrainBus.RunAsync<LanguageExt.Unit>(
                new CorruptedDataRepairInput { TableName = "player_sessions" }
            );
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        caught.Should().NotBeNull();
        caught.Should().BeOfType<InvalidOperationException>();
        caught!.Message.Should().Contain("data corruption too severe");
        caught.Message.Should().NotStartWith("{");
    }

    [Test]
    public async Task CorruptedDataRepair_ExceptionThrownToCaller_HasTrainExceptionData()
    {
        // The exception should carry structured junction context via Exception.Data
        Exception? caught = null;
        try
        {
            await TrainBus.RunAsync<LanguageExt.Unit>(
                new CorruptedDataRepairInput { TableName = "player_sessions" }
            );
        }
        catch (Exception ex)
        {
            caught = ex;
        }

        caught.Should().NotBeNull();
        var data = caught!.Data["TrainExceptionData"] as TrainExceptionData;
        data.Should().NotBeNull();
        data!.Junction.Should().Be("AttemptRepairJunction");
        data.Type.Should().Be("InvalidOperationException");
        data.Message.Should().Contain("data corruption too severe");
        data.StackTrace.Should().Contain("AttemptRepairJunction");
    }

    [Test]
    public async Task CorruptedDataRepair_FailureFieldsPersisted_ReadBackFromPostgres()
    {
        try
        {
            await TrainBus.RunAsync<LanguageExt.Unit>(
                new CorruptedDataRepairInput { TableName = "player_sessions" }
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

        // Read back from Postgres with a fresh query to confirm persistence
        var persisted = await DataContext
            .Metadatas.AsNoTracking()
            .FirstAsync(m => m.Id == metadata.Id);

        persisted.TrainState.Should().Be(TrainState.Failed);
        persisted.FailureException.Should().Be("InvalidOperationException");
        persisted.FailureJunction.Should().Be("AttemptRepairJunction");
        persisted.FailureReason.Should().Contain("data corruption too severe");
        persisted.StackTrace.Should().Contain("AttemptRepairJunction");
        persisted.EndTime.Should().NotBeNull();
    }

    #endregion
}
