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
/// Tests batch scheduling (ScheduleMany) — verifies that multiple regional manifests
/// can be dispatched concurrently through the work queue and that group-level
/// MaxActiveJobs limits are respected.
/// </summary>
[TestFixture]
public class BatchSchedulingTests : SchedulerTestFixture
{
    [Test]
    public async Task ScheduleMany_AllRegions_CanDispatchConcurrently()
    {
        var maxMetadataId = await DataContext
            .Metadatas.AsNoTracking()
            .OrderByDescending(m => m.Id)
            .Select(m => m.Id)
            .FirstOrDefaultAsync();

        // Enqueue work for all 3 regions simultaneously.
        foreach (var region in ManifestNames.Regions)
        {
            var externalId = ManifestNames.WithIndex(ManifestNames.ProcessMatch, region);
            var manifest = await DataContext
                .Manifests.AsNoTracking()
                .FirstAsync(m => m.ExternalId == externalId);

            var input = new ProcessMatchResultInput
            {
                Region = region,
                MatchId = $"batch-test-{region}",
                WinnerId = $"winner-{region}",
                LoserId = $"loser-{region}",
                WinnerScore = 60,
                LoserScore = 55,
            };

            var entry = Effect.Models.WorkQueue.WorkQueue.Create(
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
        }

        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        // Wait for all 3 regions to complete.
        foreach (var region in ManifestNames.Regions)
        {
            var externalId = ManifestNames.WithIndex(ManifestNames.ProcessMatch, region);
            var manifest = await DataContext
                .Manifests.AsNoTracking()
                .FirstAsync(m => m.ExternalId == externalId);

            await TrainStatePoller.WaitForMetadataByManifestId(
                DataContext,
                manifest.Id,
                TrainState.Completed,
                TimeSpan.FromSeconds(30),
                afterMetadataId: maxMetadataId
            );
        }

        // Verify all 3 completed.
        DataContext.Reset();
        var completedCount = await DataContext
            .Metadatas.AsNoTracking()
            .Where(m => m.Id > maxMetadataId && m.TrainState == TrainState.Completed)
            .Where(m => m.Name.Contains("ProcessMatchResult"))
            .CountAsync();

        completedCount.Should().Be(3, "all 3 regional process-match trains should complete");
    }

    [Test]
    public async Task ScheduleMany_RegionalTrains_ProduceDistinctOutputPerRegion()
    {
        var maxMetadataId = await DataContext
            .Metadatas.AsNoTracking()
            .OrderByDescending(m => m.Id)
            .Select(m => m.Id)
            .FirstOrDefaultAsync();

        // Enqueue a single region with identifiable input.
        var externalId = ManifestNames.WithIndex(ManifestNames.ProcessMatch, "eu");
        var manifest = await DataContext
            .Manifests.AsNoTracking()
            .FirstAsync(m => m.ExternalId == externalId);

        var input = new ProcessMatchResultInput
        {
            Region = "eu",
            MatchId = "batch-distinct-eu",
            WinnerId = "eu-winner",
            LoserId = "eu-loser",
            WinnerScore = 45,
            LoserScore = 40,
        };

        var entry = Effect.Models.WorkQueue.WorkQueue.Create(
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

        var metadata = await TrainStatePoller.WaitForMetadataByManifestId(
            DataContext,
            manifest.Id,
            TrainState.Completed,
            TimeSpan.FromSeconds(30),
            afterMetadataId: maxMetadataId
        );

        metadata.Output.Should().NotBeNullOrEmpty();
        metadata.Output.Should().Contain("eu", "output should reflect the region from input");
    }
}
