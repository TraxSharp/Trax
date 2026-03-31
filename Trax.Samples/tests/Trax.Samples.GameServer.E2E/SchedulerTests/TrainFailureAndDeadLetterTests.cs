using Microsoft.EntityFrameworkCore;
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
}
