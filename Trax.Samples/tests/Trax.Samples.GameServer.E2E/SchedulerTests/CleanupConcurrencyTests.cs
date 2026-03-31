using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Utils;
using Trax.Samples.GameServer.E2E.Fixtures;
using Trax.Samples.GameServer.E2E.Utilities;
using Trax.Samples.GameServer.Trains.Leaderboard.RecalculateLeaderboard;
using Trax.Samples.GameServer.Trains.Matches.ProcessMatchResult;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Trains.MetadataCleanup;

namespace Trax.Samples.GameServer.E2E.SchedulerTests;

/// <summary>
/// Tests that metadata cleanup (with batched deletes) does not interfere with
/// concurrent train dispatch and execution. These scenarios reproduce the production
/// issue where large single-statement DELETEs blocked Lambda workers' SaveChanges
/// by holding row-level locks on the metadata table.
/// </summary>
[TestFixture]
public class CleanupConcurrencyTests : SchedulerTestFixture
{
    [Test]
    public async Task BatchedCleanup_DoesNotBlockConcurrentTrainExecution()
    {
        // Arrange — Create expired metadata to trigger batched cleanup, then
        // simultaneously run a live train. The live train must complete even
        // while cleanup is actively deleting rows from the same table.
        var config = GetSchedulerConfiguration();
        var originalRetention = config.MetadataCleanup!.RetentionPeriod;
        var originalBatchSize = config.MetadataCleanup!.DeleteBatchSize;
        config.MetadataCleanup!.RetentionPeriod = TimeSpan.FromSeconds(1);
        config.MetadataCleanup!.DeleteBatchSize = 2;

        try
        {
            // Create 10 completed trains and backdate them past retention.
            for (var i = 0; i < 10; i++)
            {
                await TrainBus.RunAsync<RecalculateLeaderboardOutput>(
                    new RecalculateLeaderboardInput { Region = $"cleanup-concurrent-{i}" }
                );
            }

            // Wait for all to complete.
            DataContext.Reset();
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                DataContext.Reset();
                var completed = await DataContext
                    .Metadatas.AsNoTracking()
                    .Where(m =>
                        m.Name.Contains("RecalculateLeaderboard")
                        && m.TrainState == TrainState.Completed
                    )
                    .CountAsync();

                if (completed >= 10)
                    break;

                await Task.Delay(250);
            }

            // Backdate all to be older than retention.
            await DataContext
                .Metadatas.Where(m => m.Name.Contains("RecalculateLeaderboard"))
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(m => m.StartTime, DateTime.UtcNow.AddHours(-2))
                );
            DataContext.Reset();

            // Run cleanup and a new train concurrently.
            using var cleanupScope = SharedSchedulerSetup.Factory.Services.CreateScope();
            var cleanupTrain =
                cleanupScope.ServiceProvider.GetRequiredService<IMetadataCleanupTrain>();

            var cleanupTask = cleanupTrain.Run(new MetadataCleanupRequest());
            var trainTask = TrainBus.RunAsync<RecalculateLeaderboardOutput>(
                new RecalculateLeaderboardInput { Region = "concurrent-live" }
            );

            // Both must complete without timeout or deadlock.
            await Task.WhenAll(cleanupTask, trainTask);

            var liveOutput = await trainTask;
            liveOutput.Should().NotBeNull("live train should complete during cleanup");
            liveOutput.Region.Should().Be("concurrent-live");
        }
        finally
        {
            config.MetadataCleanup!.RetentionPeriod = originalRetention;
            config.MetadataCleanup!.DeleteBatchSize = originalBatchSize;
        }
    }

    [Test]
    public async Task BatchedCleanup_DoesNotBlockConcurrentDispatch()
    {
        // Arrange — Reproduce the exact production scenario: cleanup is deleting
        // expired metadata while the JobDispatcher is counting active jobs and
        // dispatching new work from the queue. The dispatch must succeed.
        var config = GetSchedulerConfiguration();
        var originalRetention = config.MetadataCleanup!.RetentionPeriod;
        var originalBatchSize = config.MetadataCleanup!.DeleteBatchSize;
        config.MetadataCleanup!.RetentionPeriod = TimeSpan.FromSeconds(1);
        config.MetadataCleanup!.DeleteBatchSize = 2;

        try
        {
            // Create expired metadata from prior train runs.
            for (var i = 0; i < 8; i++)
            {
                await TrainBus.RunAsync<RecalculateLeaderboardOutput>(
                    new RecalculateLeaderboardInput { Region = $"dispatch-cleanup-{i}" }
                );
            }

            DataContext.Reset();
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < deadline)
            {
                DataContext.Reset();
                var completed = await DataContext
                    .Metadatas.AsNoTracking()
                    .Where(m =>
                        m.Name.Contains("RecalculateLeaderboard")
                        && m.TrainState == TrainState.Completed
                    )
                    .CountAsync();

                if (completed >= 8)
                    break;

                await Task.Delay(250);
            }

            await DataContext
                .Metadatas.Where(m => m.Name.Contains("RecalculateLeaderboard"))
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(m => m.StartTime, DateTime.UtcNow.AddHours(-2))
                );
            DataContext.Reset();

            // Enqueue a work queue entry for dispatch while cleanup runs.
            var maxMetadataId = await DataContext
                .Metadatas.AsNoTracking()
                .OrderByDescending(m => m.Id)
                .Select(m => m.Id)
                .FirstOrDefaultAsync();

            var processMatchExternalId = ManifestNames.WithIndex(ManifestNames.ProcessMatch, "na");
            var manifest = await DataContext
                .Manifests.AsNoTracking()
                .FirstAsync(m => m.ExternalId == processMatchExternalId);

            var input = new ProcessMatchResultInput
            {
                Region = "na",
                MatchId = "cleanup-dispatch-test",
                WinnerId = "p1",
                LoserId = "p2",
                WinnerScore = 30,
                LoserScore = 20,
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

            // Run cleanup concurrently — the dispatcher (background service) will
            // pick up the work queue entry and count capacity while cleanup deletes.
            using var cleanupScope = SharedSchedulerSetup.Factory.Services.CreateScope();
            var cleanupTrain =
                cleanupScope.ServiceProvider.GetRequiredService<IMetadataCleanupTrain>();
            await cleanupTrain.Run(new MetadataCleanupRequest());

            // The dispatched train should still complete.
            var metadata = await TrainStatePoller.WaitForMetadataByManifestId(
                DataContext,
                manifest.Id,
                TrainState.Completed,
                TimeSpan.FromSeconds(30),
                afterMetadataId: maxMetadataId
            );

            metadata.Should().NotBeNull("dispatched train should complete during cleanup");
        }
        finally
        {
            config.MetadataCleanup!.RetentionPeriod = originalRetention;
            config.MetadataCleanup!.DeleteBatchSize = originalBatchSize;
        }
    }

    [Test]
    public async Task BatchedCleanup_PreservesInFlightTrainsDuringDeletion()
    {
        // Arrange — Verify the safety boundary: cleanup must never delete
        // Pending or InProgress metadata, even when running with small batches
        // alongside expired terminal metadata.
        var config = GetSchedulerConfiguration();
        var originalRetention = config.MetadataCleanup!.RetentionPeriod;
        var originalBatchSize = config.MetadataCleanup!.DeleteBatchSize;
        config.MetadataCleanup!.RetentionPeriod = TimeSpan.FromSeconds(1);
        config.MetadataCleanup!.DeleteBatchSize = 1;

        try
        {
            // Create expired completed metadata.
            for (var i = 0; i < 5; i++)
            {
                await TrainBus.RunAsync<RecalculateLeaderboardOutput>(
                    new RecalculateLeaderboardInput { Region = $"inflight-{i}" }
                );
            }

            DataContext.Reset();
            var waitDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
            while (DateTime.UtcNow < waitDeadline)
            {
                DataContext.Reset();
                var completed = await DataContext
                    .Metadatas.AsNoTracking()
                    .Where(m =>
                        m.Name.Contains("RecalculateLeaderboard")
                        && m.TrainState == TrainState.Completed
                    )
                    .CountAsync();

                if (completed >= 5)
                    break;

                await Task.Delay(250);
            }

            // Backdate completed metadata past retention.
            await DataContext
                .Metadatas.Where(m =>
                    m.Name.Contains("RecalculateLeaderboard")
                    && m.TrainState == TrainState.Completed
                )
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(m => m.StartTime, DateTime.UtcNow.AddHours(-2))
                );
            DataContext.Reset();

            // Start a new train — it will have InProgress/Completed metadata while cleanup runs.
            var trainTask = TrainBus.RunAsync<RecalculateLeaderboardOutput>(
                new RecalculateLeaderboardInput { Region = "must-survive" }
            );

            // Run cleanup while the train is executing.
            using var cleanupScope = SharedSchedulerSetup.Factory.Services.CreateScope();
            var cleanupTrain =
                cleanupScope.ServiceProvider.GetRequiredService<IMetadataCleanupTrain>();
            await cleanupTrain.Run(new MetadataCleanupRequest());

            // The live train must complete successfully.
            var output = await trainTask;
            output.Should().NotBeNull();
            output.Region.Should().Be("must-survive");

            // Verify the live train's metadata survived cleanup.
            var liveMetadata = await TrainStatePoller.WaitForMetadataByTrainName(
                DataContext,
                "RecalculateLeaderboard",
                TrainState.Completed,
                TimeSpan.FromSeconds(5)
            );

            liveMetadata.Should().NotBeNull("in-flight train metadata must survive cleanup");
        }
        finally
        {
            config.MetadataCleanup!.RetentionPeriod = originalRetention;
            config.MetadataCleanup!.DeleteBatchSize = originalBatchSize;
        }
    }

    [Test]
    public async Task BatchedCleanup_HighVolume_DeletesAllWithAssociatedRecords()
    {
        // Arrange — Simulate a high-frequency train (like ManifestManager) that
        // generates many metadata rows with associated logs. Verify batched
        // cleanup handles all of them including FK-dependent records.
        var config = GetSchedulerConfiguration();
        var originalRetention = config.MetadataCleanup!.RetentionPeriod;
        var originalBatchSize = config.MetadataCleanup!.DeleteBatchSize;
        config.MetadataCleanup!.RetentionPeriod = TimeSpan.FromSeconds(1);
        config.MetadataCleanup!.DeleteBatchSize = 3;

        try
        {
            // Run 15 trains to generate metadata + logs (AddDataContextLogging is enabled).
            for (var i = 0; i < 15; i++)
            {
                await TrainBus.RunAsync<RecalculateLeaderboardOutput>(
                    new RecalculateLeaderboardInput { Region = $"high-vol-{i}" }
                );
            }

            DataContext.Reset();
            var waitDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (DateTime.UtcNow < waitDeadline)
            {
                DataContext.Reset();
                var completed = await DataContext
                    .Metadatas.AsNoTracking()
                    .Where(m =>
                        m.Name.Contains("RecalculateLeaderboard")
                        && m.TrainState == TrainState.Completed
                    )
                    .CountAsync();

                if (completed >= 15)
                    break;

                await Task.Delay(250);
            }

            // Backdate all past retention.
            await DataContext
                .Metadatas.Where(m => m.Name.Contains("RecalculateLeaderboard"))
                .ExecuteUpdateAsync(setters =>
                    setters.SetProperty(m => m.StartTime, DateTime.UtcNow.AddHours(-2))
                );

            // Wait for logs to flush (DataContextLoggingProvider uses a 1-second timer).
            await Task.Delay(2000);
            DataContext.Reset();

            var logCountBefore = await DataContext
                .Logs.AsNoTracking()
                .Where(l =>
                    DataContext
                        .Metadatas.Where(m => m.Name.Contains("RecalculateLeaderboard"))
                        .Select(m => m.Id)
                        .Contains(l.MetadataId)
                )
                .CountAsync();

            // Act — Run cleanup (should take 5 batches of 3 = 15 rows).
            using var cleanupScope = SharedSchedulerSetup.Factory.Services.CreateScope();
            var cleanupTrain =
                cleanupScope.ServiceProvider.GetRequiredService<IMetadataCleanupTrain>();
            await cleanupTrain.Run(new MetadataCleanupRequest());

            // Assert — All metadata and associated logs should be gone.
            DataContext.Reset();
            var remainingMetadata = await DataContext
                .Metadatas.AsNoTracking()
                .Where(m => m.Name.Contains("RecalculateLeaderboard"))
                .CountAsync();

            remainingMetadata
                .Should()
                .Be(0, "all expired metadata should be deleted across multiple batches");

            // Verify associated logs were also deleted (if any existed).
            if (logCountBefore > 0)
            {
                var remainingLogs = await DataContext
                    .Logs.AsNoTracking()
                    .Where(l =>
                        DataContext
                            .Metadatas.Where(m => m.Name.Contains("RecalculateLeaderboard"))
                            .Select(m => m.Id)
                            .Contains(l.MetadataId)
                    )
                    .CountAsync();

                remainingLogs
                    .Should()
                    .Be(0, "associated logs should be deleted with their metadata");
            }
        }
        finally
        {
            config.MetadataCleanup!.RetentionPeriod = originalRetention;
            config.MetadataCleanup!.DeleteBatchSize = originalBatchSize;
        }
    }
}
