using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Enums;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Utils;
using Trax.Samples.GameServer.E2E.Fixtures;
using Trax.Samples.GameServer.E2E.Utilities;
using Trax.Samples.GameServer.Trains.Matches.ProcessMatchResult;

namespace Trax.Samples.GameServer.E2E.SchedulerTests;

/// <summary>
/// Tests dormant dependent activation — the exact code path where Bug 3
/// (DataContext graph cascade) caused Lambda workers to hang.
///
/// Flow: ProcessMatchResult work queue entry → JobDispatcher → JobRunnerTrain →
/// LoadMetadataJunction (with .Include(Manifest)) → child scope Track →
/// CheckForAnomaliesJunction detects anomalies → ActivateAsync enqueues
/// DetectCheatPattern → scheduler picks it up and runs it.
///
/// This MUST go through the work queue, not TrainBus.RunAsync, because
/// IDormantDependentContext only activates within a scheduled execution context.
/// </summary>
[TestFixture]
public class DormantDependentTests : SchedulerTestFixture
{
    [Test]
    public async Task ProcessMatchResult_HighScoreDiff_ActivatesDetectCheatPattern()
    {
        // Record the highest metadata ID before the test, so we can filter for new ones.
        var maxMetadataId = await DataContext
            .Metadatas.AsNoTracking()
            .OrderByDescending(m => m.Id)
            .Select(m => m.Id)
            .FirstOrDefaultAsync();

        // Find the process-match-na manifest to enqueue against it.
        var processMatchExternalId = ManifestNames.WithIndex(ManifestNames.ProcessMatch, "na");
        var manifest = await DataContext
            .Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == processMatchExternalId);

        // Enqueue a work queue entry with high score diff (> 50 triggers anomaly detection).
        var input = new ProcessMatchResultInput
        {
            Region = "na",
            MatchId = "e2e-cheat-001",
            WinnerId = "player-1",
            LoserId = "player-2",
            WinnerScore = 100,
            LoserScore = 10,
        };

        var entry = Trax.Effect.Models.WorkQueue.WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = manifest.Name,
                Input = JsonSerializer.Serialize(
                    input,
                    TraxJsonSerializationOptions.ManifestProperties
                ),
                InputTypeName = typeof(ProcessMatchResultInput).FullName,
                ManifestId = manifest.Id,
                Priority = 20,
            }
        );

        await DataContext.Track(entry);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Wait for ProcessMatchResult to complete (scheduler dispatches work queue entry).
        await TrainStatePoller.WaitForMetadataByManifestId(
            DataContext,
            manifest.Id,
            TrainState.Completed,
            TimeSpan.FromSeconds(30),
            afterMetadataId: maxMetadataId
        );

        // The dormant dependent (detect-cheat-na) should now be activated and executed.
        var metadata = await TrainStatePoller.WaitForMetadataByTrainName(
            DataContext,
            "DetectCheatPattern",
            TrainState.Completed,
            TimeSpan.FromSeconds(30),
            afterMetadataId: maxMetadataId
        );

        metadata.Should().NotBeNull();
    }

    [Test]
    public async Task ProcessMatchResult_LowScoreDiff_DoesNotActivateDormantDependent()
    {
        var maxMetadataId = await DataContext
            .Metadatas.AsNoTracking()
            .OrderByDescending(m => m.Id)
            .Select(m => m.Id)
            .FirstOrDefaultAsync();

        // Find the process-match-eu manifest.
        var processMatchExternalId = ManifestNames.WithIndex(ManifestNames.ProcessMatch, "eu");
        var manifest = await DataContext
            .Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == processMatchExternalId);

        // Enqueue with low score difference (< 50, no anomalies).
        var input = new ProcessMatchResultInput
        {
            Region = "eu",
            MatchId = "e2e-no-cheat-001",
            WinnerId = "player-3",
            LoserId = "player-4",
            WinnerScore = 60,
            LoserScore = 55,
        };

        var entry = Trax.Effect.Models.WorkQueue.WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = manifest.Name,
                Input = JsonSerializer.Serialize(
                    input,
                    TraxJsonSerializationOptions.ManifestProperties
                ),
                InputTypeName = typeof(ProcessMatchResultInput).FullName,
                ManifestId = manifest.Id,
                Priority = 20,
            }
        );

        await DataContext.Track(entry);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Wait for ProcessMatchResult to complete.
        await TrainStatePoller.WaitForMetadataByManifestId(
            DataContext,
            manifest.Id,
            TrainState.Completed,
            TimeSpan.FromSeconds(30),
            afterMetadataId: maxMetadataId
        );

        // Wait a short time and verify no DetectCheatPattern metadata appeared.
        await TrainStatePoller.EnsureNoMetadataAppears(
            DataContext,
            "DetectCheatPattern",
            TimeSpan.FromSeconds(5),
            afterMetadataId: maxMetadataId
        );
    }
}
