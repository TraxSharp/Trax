using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Enums;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Utils;
using Trax.Samples.GameServer.E2E.Fixtures;
using Trax.Samples.GameServer.E2E.Utilities;
using Trax.Samples.GameServer.Trains.Leaderboard.RecalculateLeaderboard;

namespace Trax.Samples.GameServer.E2E.SchedulerTests;

[TestFixture]
public class DependencyChainTests : SchedulerTestFixture
{
    [Test]
    public async Task RecalculateLeaderboard_TriggersGenerateSeasonReport()
    {
        var maxMetadataId = await DataContext
            .Metadatas.AsNoTracking()
            .OrderByDescending(m => m.Id)
            .Select(m => m.Id)
            .FirstOrDefaultAsync();

        // Re-enable ManifestManager — it needs to detect parent completion
        // and enqueue the dependent GenerateSeasonReport.
        EnableManifestManager();

        try
        {
            // Find the recalculate-leaderboard manifest.
            var parentManifest = await DataContext
                .Manifests.AsNoTracking()
                .FirstAsync(m => m.ExternalId == ManifestNames.RecalculateLeaderboard);

            // Enqueue a work queue entry for RecalculateLeaderboard.
            var input = new RecalculateLeaderboardInput { Region = "global" };

            var entry = Trax.Effect.Models.WorkQueue.WorkQueue.Create(
                new CreateWorkQueue
                {
                    TrainName = parentManifest.Name,
                    Input = JsonSerializer.Serialize(
                        input,
                        TraxJsonSerializationOptions.ManifestProperties
                    ),
                    InputTypeName = typeof(RecalculateLeaderboardInput).FullName,
                    ManifestId = parentManifest.Id,
                    Priority = 20,
                }
            );

            await DataContext.Track(entry);
            await DataContext.SaveChanges(CancellationToken.None);
            DataContext.Reset();

            // Wait for RecalculateLeaderboard to complete via the scheduler.
            await TrainStatePoller.WaitForMetadataByManifestId(
                DataContext,
                parentManifest.Id,
                TrainState.Completed,
                TimeSpan.FromSeconds(30),
                afterMetadataId: maxMetadataId
            );

            // The ManifestManager should detect that recalculate-leaderboard has a newer
            // LastSuccessfulRun than generate-season-report, and enqueue the dependent.
            var reportManifest = await DataContext
                .Manifests.AsNoTracking()
                .FirstAsync(m => m.ExternalId == ManifestNames.GenerateSeasonReport);

            var metadata = await TrainStatePoller.WaitForMetadataByManifestId(
                DataContext,
                reportManifest.Id,
                TrainState.Completed,
                TimeSpan.FromSeconds(30),
                afterMetadataId: maxMetadataId
            );

            metadata.Should().NotBeNull();
            metadata.Name.Should().Contain("GenerateSeasonReport");
        }
        finally
        {
            DisableManifestManager();
        }
    }
}
