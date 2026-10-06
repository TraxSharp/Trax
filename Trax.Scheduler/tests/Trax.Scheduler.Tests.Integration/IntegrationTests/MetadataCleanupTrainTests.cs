using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Enums;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.DeadLetter.DTOs;
using Trax.Effect.Models.Log;
using Trax.Effect.Models.Log.DTOs;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.JobDispatcher;
using Trax.Scheduler.Trains.ManifestManager;
using Trax.Scheduler.Trains.MetadataCleanup;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Integration tests for the MetadataCleanupTrain which deletes expired metadata
/// entries for whitelisted train types.
/// </summary>
[TestFixture]
public class MetadataCleanupTrainTests : TestSetup
{
    private IMetadataCleanupTrain _train = null!;
    private SchedulerConfiguration _config = null!;

    public override async Task TestSetUp()
    {
        await base.TestSetUp();
        _train = Scope.ServiceProvider.GetRequiredService<IMetadataCleanupTrain>();
        _config = Scope.ServiceProvider.GetRequiredService<SchedulerConfiguration>();
    }

    [TearDown]
    public async Task MetadataCleanupTrainTestsTearDown()
    {
        // Reset batch size to default for test isolation
        _config.MetadataCleanup!.DeleteBatchSize = 1000;

        if (_train is IDisposable disposable)
            disposable.Dispose();
    }

    #region Default Configuration Tests

    [Test]
    public void DefaultWhitelist_ContainsManifestManagerTrain()
    {
        _config
            .MetadataCleanup!.TrainTypeWhitelist.Should()
            .Contain(
                typeof(ManifestManagerTrain).FullName!,
                "ManifestManagerTrain should be in the default whitelist"
            );
    }

    [Test]
    public void DefaultWhitelist_ContainsMetadataCleanupTrain()
    {
        _config
            .MetadataCleanup!.TrainTypeWhitelist.Should()
            .Contain(
                typeof(MetadataCleanupTrain).FullName!,
                "MetadataCleanupTrain should be in the default whitelist"
            );
    }

    [Test]
    public void DefaultRetentionPeriod_IsThirtyMinutes()
    {
        _config
            .MetadataCleanup!.RetentionPeriod.Should()
            .Be(TimeSpan.FromMinutes(30), "default retention period should be 30 minutes");
    }

    [Test]
    public void DefaultCleanupInterval_IsOneMinute()
    {
        _config
            .MetadataCleanup!.CleanupInterval.Should()
            .Be(TimeSpan.FromMinutes(1), "default cleanup interval should be 1 minute");
    }

    #endregion

    #region Internal Train Pruning

    [Test]
    public async Task Run_PrunesEveryAdminTrainByDefault()
    {
        // Every internal scheduler train must be cleaned up without the consumer having to
        // whitelist it. JobDispatcher (a metadata row every poll) is the one that caused the
        // outage this guards against. If a new admin train is added to AdminTrains without the
        // cleanup covering it, this fails.
        var seeded = new List<long>();
        foreach (var adminName in AdminTrains.FullNames)
        {
            var metadata = await CreateAndSaveMetadata(
                name: adminName,
                state: TrainState.Completed,
                startTime: DateTime.UtcNow.AddHours(-2)
            );
            seeded.Add(metadata.Id);
        }

        await _train.Run(new MetadataCleanupRequest());

        DataContext.Reset();
        var remaining = await DataContext.Metadatas.Where(m => seeded.Contains(m.Id)).CountAsync();

        remaining
            .Should()
            .Be(0, "every internal scheduler train's metadata should be pruned by default");
    }

    [Test]
    public async Task Run_DeletesJobDispatcherMetadata_EvenThoughNotInWhitelist()
    {
        // The configurable whitelist never contains JobDispatcher, yet its metadata must be
        // pruned: it persists a row on every dispatch poll and is the dominant source of growth.
        _config
            .MetadataCleanup!.TrainTypeWhitelist.Should()
            .NotContain(
                typeof(JobDispatcherTrain).FullName!,
                "JobDispatcher is pruned unconditionally, not via the whitelist"
            );

        var metadata = await CreateAndSaveMetadata(
            name: typeof(JobDispatcherTrain).FullName!,
            state: TrainState.Completed,
            startTime: DateTime.UtcNow.AddHours(-2)
        );

        await _train.Run(new MetadataCleanupRequest());

        DataContext.Reset();
        var remaining = await DataContext
            .Metadatas.Where(m => m.Id == metadata.Id)
            .FirstOrDefaultAsync();

        remaining
            .Should()
            .BeNull("JobDispatcher metadata must be pruned even though it is not whitelisted");
    }

    #endregion

    #region Foreign Key Reference Tests

    [Test]
    public async Task Run_MetadataReferencedByDeadLetterRetry_ClearsRefAndDeletesMetadata()
    {
        // Reproduces the incident: a dead letter's retry_metadata_id pointed at an expired
        // metadata row, and the RESTRICT foreign key made the whole cleanup DELETE throw.
        var manifest = await CreateAndSaveManifest();
        var metadata = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Completed,
            startTime: DateTime.UtcNow.AddHours(-2)
        );
        var deadLetter = await CreateAndSaveDeadLetter(manifest);

        await DataContext
            .DeadLetters.Where(d => d.Id == deadLetter.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.RetryMetadataId, metadata.Id));
        DataContext.Reset();

        await _train.Run(new MetadataCleanupRequest());

        DataContext.Reset();
        var remainingMetadata = await DataContext
            .Metadatas.Where(m => m.Id == metadata.Id)
            .FirstOrDefaultAsync();
        var survivingDeadLetter = await DataContext
            .DeadLetters.Where(d => d.Id == deadLetter.Id)
            .FirstOrDefaultAsync();

        remainingMetadata
            .Should()
            .BeNull(
                "the referenced metadata should be deleted after its back-reference is cleared"
            );
        survivingDeadLetter
            .Should()
            .NotBeNull("the dead letter is a meaningful record and must survive the cleanup");
        survivingDeadLetter!
            .RetryMetadataId.Should()
            .BeNull(
                "the dangling retry reference should be nulled, not left pointing at a deleted row"
            );
    }

    [Test]
    public async Task Run_ParentMetadataWithRunningChild_NullsChildParentIdAndDeletesParent()
    {
        var parent = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Completed,
            startTime: DateTime.UtcNow.AddHours(-2)
        );

        // A still-running child (not eligible for deletion) references the parent via parent_id.
        var child = await CreateAndSaveChildMetadata(
            name: "Consumer.Trains.SomeChildTrain",
            state: TrainState.InProgress,
            parentId: parent.Id
        );

        await _train.Run(new MetadataCleanupRequest());

        DataContext.Reset();
        var remainingParent = await DataContext
            .Metadatas.Where(m => m.Id == parent.Id)
            .FirstOrDefaultAsync();
        var survivingChild = await DataContext
            .Metadatas.Where(m => m.Id == child.Id)
            .FirstOrDefaultAsync();

        remainingParent
            .Should()
            .BeNull(
                "the expired parent should be deleted after the child's parent reference is cleared"
            );
        survivingChild.Should().NotBeNull("the still-running child must survive the cleanup");
        survivingChild!.ParentId.Should().BeNull("the dangling parent reference should be nulled");
    }

    [Test]
    public async Task Run_WithFkReferencedRowsAcrossBatches_DeletesAll()
    {
        // Batch size 1 forces the per-row delete path, so every row exercises the FK-clearing.
        _config.MetadataCleanup!.DeleteBatchSize = 1;

        var manifest = await CreateAndSaveManifest();
        var ids = new List<long>();
        for (var i = 0; i < 5; i++)
        {
            var metadata = await CreateAndSaveMetadata(
                name: typeof(ManifestManagerTrain).FullName!,
                state: TrainState.Completed,
                startTime: DateTime.UtcNow.AddHours(-2)
            );
            ids.Add(metadata.Id);

            var deadLetter = await CreateAndSaveDeadLetter(manifest);
            await DataContext
                .DeadLetters.Where(d => d.Id == deadLetter.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.RetryMetadataId, metadata.Id));
            DataContext.Reset();
        }

        await _train.Run(new MetadataCleanupRequest());

        DataContext.Reset();
        var remaining = await DataContext.Metadatas.Where(m => ids.Contains(m.Id)).CountAsync();

        remaining
            .Should()
            .Be(
                0,
                "every expired row should be deleted even when each carries a dead-letter back-reference"
            );
    }

    #endregion

    #region Deletion Tests - Terminal States

    [Test]
    public async Task Run_DeletesExpiredCompletedMetadata()
    {
        // Arrange - Create completed metadata older than retention period
        var metadata = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Completed,
            startTime: DateTime.UtcNow.AddHours(-2)
        );

        // Act
        await _train.Run(new MetadataCleanupRequest());

        // Assert
        DataContext.Reset();
        var remaining = await DataContext
            .Metadatas.Where(m => m.Id == metadata.Id)
            .FirstOrDefaultAsync();

        remaining.Should().BeNull("expired completed metadata should be deleted");
    }

    [Test]
    public async Task Run_DeletesExpiredFailedMetadata()
    {
        // Arrange - Create failed metadata older than retention period
        var metadata = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Failed,
            startTime: DateTime.UtcNow.AddHours(-2)
        );

        // Act
        await _train.Run(new MetadataCleanupRequest());

        // Assert
        DataContext.Reset();
        var remaining = await DataContext
            .Metadatas.Where(m => m.Id == metadata.Id)
            .FirstOrDefaultAsync();

        remaining.Should().BeNull("expired failed metadata should be deleted");
    }

    #endregion

    #region Retention Period Tests

    [Test]
    public async Task Run_DoesNotDeleteRecentMetadata()
    {
        // Arrange - Create completed metadata within retention period
        var metadata = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Completed,
            startTime: DateTime.UtcNow.AddMinutes(-15) // 15 min ago, within 30 minute retention
        );

        // Act
        await _train.Run(new MetadataCleanupRequest());

        // Assert
        DataContext.Reset();
        var remaining = await DataContext
            .Metadatas.Where(m => m.Id == metadata.Id)
            .FirstOrDefaultAsync();

        remaining
            .Should()
            .NotBeNull("recent metadata within retention period should not be deleted");
    }

    #endregion

    #region Whitelist Filtering Tests

    [Test]
    public async Task Run_DoesNotDeleteNonWhitelistedMetadata()
    {
        // Arrange - Create old completed metadata for a non-whitelisted train
        var metadata = await CreateAndSaveMetadata(
            name: "SomeOtherTrain",
            state: TrainState.Completed,
            startTime: DateTime.UtcNow.AddHours(-2)
        );

        // Act
        await _train.Run(new MetadataCleanupRequest());

        // Assert
        DataContext.Reset();
        var remaining = await DataContext
            .Metadatas.Where(m => m.Id == metadata.Id)
            .FirstOrDefaultAsync();

        remaining.Should().NotBeNull("metadata for non-whitelisted trains should not be deleted");
    }

    [Test]
    public async Task Run_DeletesMetadataForAllWhitelistedTypes()
    {
        // Arrange - Create expired metadata for both default whitelisted types
        var managerMetadata = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Completed,
            startTime: DateTime.UtcNow.AddHours(-2)
        );

        var cleanupMetadata = await CreateAndSaveMetadata(
            name: typeof(MetadataCleanupTrain).FullName!,
            state: TrainState.Completed,
            startTime: DateTime.UtcNow.AddHours(-2)
        );

        // Act
        await _train.Run(new MetadataCleanupRequest());

        // Assert
        DataContext.Reset();
        var managerRemaining = await DataContext
            .Metadatas.Where(m => m.Id == managerMetadata.Id)
            .FirstOrDefaultAsync();
        var cleanupRemaining = await DataContext
            .Metadatas.Where(m => m.Id == cleanupMetadata.Id)
            .FirstOrDefaultAsync();

        managerRemaining.Should().BeNull("expired ManifestManagerTrain metadata should be deleted");
        cleanupRemaining.Should().BeNull("expired MetadataCleanupTrain metadata should be deleted");
    }

    #endregion

    #region Non-Terminal State Tests

    [Test]
    public async Task Run_DoesNotDeletePendingMetadata()
    {
        // Arrange - Create old pending metadata (non-terminal state)
        var metadata = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Pending,
            startTime: DateTime.UtcNow.AddHours(-2)
        );

        // Act
        await _train.Run(new MetadataCleanupRequest());

        // Assert
        DataContext.Reset();
        var remaining = await DataContext
            .Metadatas.Where(m => m.Id == metadata.Id)
            .FirstOrDefaultAsync();

        remaining.Should().NotBeNull("pending metadata should never be deleted regardless of age");
    }

    [Test]
    public async Task Run_DoesNotDeleteInProgressMetadata()
    {
        // Arrange - Create old in-progress metadata (non-terminal state)
        var metadata = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.InProgress,
            startTime: DateTime.UtcNow.AddHours(-2)
        );

        // Act
        await _train.Run(new MetadataCleanupRequest());

        // Assert
        DataContext.Reset();
        var remaining = await DataContext
            .Metadatas.Where(m => m.Id == metadata.Id)
            .FirstOrDefaultAsync();

        remaining
            .Should()
            .NotBeNull("in-progress metadata should never be deleted regardless of age");
    }

    #endregion

    #region Associated Logs Tests

    [Test]
    public async Task Run_DeletesAssociatedLogs()
    {
        // Arrange - Create expired metadata with associated logs
        var metadata = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Completed,
            startTime: DateTime.UtcNow.AddHours(-2)
        );

        var log = Log.Create(
            new CreateLog
            {
                Level = LogLevel.Information,
                Message = "Test log entry",
                CategoryName = "TestCategory",
                EventId = 1,
            }
        );

        // Set MetadataId by tracking the log in the context
        await DataContext.Logs.AddAsync(log);

        // Use raw SQL to set the metadata_id since MetadataId has a private setter
        await DataContext.SaveChanges(CancellationToken.None);
        var logId = log.Id;

        await DataContext
            .Logs.Where(l => l.Id == logId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(l => l.MetadataId, metadata.Id));

        DataContext.Reset();

        // Act
        await _train.Run(new MetadataCleanupRequest());

        // Assert
        DataContext.Reset();
        var remainingLog = await DataContext.Logs.Where(l => l.Id == logId).FirstOrDefaultAsync();
        var remainingMetadata = await DataContext
            .Metadatas.Where(m => m.Id == metadata.Id)
            .FirstOrDefaultAsync();

        remainingLog
            .Should()
            .BeNull("logs associated with deleted metadata should also be deleted");
        remainingMetadata.Should().BeNull("the metadata itself should be deleted");
    }

    #endregion

    #region Associated Work Queue Tests

    [Test]
    public async Task Run_DeletesAssociatedWorkQueueEntries()
    {
        // Arrange - Create expired metadata with an associated dispatched work queue entry
        var metadata = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Completed,
            startTime: DateTime.UtcNow.AddHours(-2)
        );

        var workQueueEntry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = typeof(ManifestManagerTrain).FullName!,
                Input = null,
                InputTypeName = null,
            }
        );

        await DataContext.Track(workQueueEntry);
        await DataContext.SaveChanges(CancellationToken.None);
        var entryId = workQueueEntry.Id;

        // Link the work queue entry to the metadata and mark as dispatched
        await DataContext
            .WorkQueues.Where(wq => wq.Id == entryId)
            .ExecuteUpdateAsync(setters =>
                setters
                    .SetProperty(wq => wq.MetadataId, metadata.Id)
                    .SetProperty(wq => wq.Status, WorkQueueStatus.Dispatched)
                    .SetProperty(wq => wq.DispatchedAt, DateTime.UtcNow)
            );

        DataContext.Reset();

        // Act
        await _train.Run(new MetadataCleanupRequest());

        // Assert
        DataContext.Reset();
        var remainingEntry = await DataContext
            .WorkQueues.Where(wq => wq.Id == entryId)
            .FirstOrDefaultAsync();
        var remainingMetadata = await DataContext
            .Metadatas.Where(m => m.Id == metadata.Id)
            .FirstOrDefaultAsync();

        remainingEntry
            .Should()
            .BeNull("work queue entries associated with deleted metadata should also be deleted");
        remainingMetadata.Should().BeNull("the metadata itself should be deleted");
    }

    [Test]
    public async Task Run_DoesNotDeleteWorkQueueEntriesForNonExpiredMetadata()
    {
        // Arrange - Create recent metadata with an associated work queue entry
        var metadata = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Completed,
            startTime: DateTime.UtcNow.AddMinutes(-10) // Within 30 minute retention
        );

        var workQueueEntry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = typeof(ManifestManagerTrain).FullName!,
                Input = null,
                InputTypeName = null,
            }
        );

        await DataContext.Track(workQueueEntry);
        await DataContext.SaveChanges(CancellationToken.None);
        var entryId = workQueueEntry.Id;

        await DataContext
            .WorkQueues.Where(wq => wq.Id == entryId)
            .ExecuteUpdateAsync(setters =>
                setters
                    .SetProperty(wq => wq.MetadataId, metadata.Id)
                    .SetProperty(wq => wq.Status, WorkQueueStatus.Dispatched)
                    .SetProperty(wq => wq.DispatchedAt, DateTime.UtcNow)
            );

        DataContext.Reset();

        // Act
        await _train.Run(new MetadataCleanupRequest());

        // Assert
        DataContext.Reset();
        var remainingEntry = await DataContext
            .WorkQueues.Where(wq => wq.Id == entryId)
            .FirstOrDefaultAsync();

        remainingEntry
            .Should()
            .NotBeNull("work queue entries for non-expired metadata should survive cleanup");
    }

    #endregion

    #region Replay Source Tests

    // A run another run will replay keeps its decisions only while its row exists: they cascade
    // with it, and a replay of a deleted run fails permanently (central docs/0041).

    [Test]
    public async Task Run_KeepsAnExpiredRunAQueuedRequeueWillReplay()
    {
        var source = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Completed,
            startTime: DateTime.UtcNow.AddHours(-2)
        );
        var requeue = await QueueReplayOf(source.Id);

        await _train.Run(new MetadataCleanupRequest());

        DataContext.Reset();
        (await DataContext.Metadatas.AnyAsync(m => m.Id == source.Id))
            .Should()
            .BeTrue("the queued requeue replays its decisions when it runs");

        // Once the entry is no longer waiting to run, nothing points at the run.
        await DataContext
            .WorkQueues.Where(q => q.Id == requeue.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.Status, WorkQueueStatus.Cancelled));

        await _train.Run(new MetadataCleanupRequest());

        DataContext.Reset();
        (await DataContext.Metadatas.AnyAsync(m => m.Id == source.Id)).Should().BeFalse();
    }

    [Test]
    public async Task Run_KeepsAnExpiredRunARetainedRunReplayed()
    {
        var source = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Completed,
            startTime: DateTime.UtcNow.AddHours(-2)
        );
        var replayer = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Failed,
            startTime: DateTime.UtcNow
        );
        await LinkReplay(replayer.Id, source.Id);

        await _train.Run(new MetadataCleanupRequest());

        DataContext.Reset();
        (await DataContext.Metadatas.AnyAsync(m => m.Id == source.Id))
            .Should()
            .BeTrue("a requeue of the retained run follows its link back to this one");
        (await DataContext.Metadatas.AnyAsync(m => m.Id == replayer.Id)).Should().BeTrue();
    }

    [TestCase(1)]
    [TestCase(1000)]
    public async Task Run_DeletesAnExpiredReplayTogetherWithTheRunItReplayed(int batchSize)
    {
        // A replay deleted alone would leave the run it replayed looking as though nothing had
        // replayed it, until a later sweep, and a retry could replay the same answers again
        // (docs/adr/0017-a-manifests-retry-replays-the-decisions-of-the-run-it-retries.md). Even
        // a batch of one takes both.
        _config.MetadataCleanup!.DeleteBatchSize = batchSize;
        var source = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Completed,
            startTime: DateTime.UtcNow.AddHours(-3)
        );
        var replayer = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Completed,
            startTime: DateTime.UtcNow.AddHours(-2)
        );
        var requeueOfReplay = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Failed,
            startTime: DateTime.UtcNow.AddHours(-1)
        );
        await LinkReplay(replayer.Id, source.Id);
        await LinkReplay(requeueOfReplay.Id, replayer.Id);

        await _train.Run(new MetadataCleanupRequest());

        DataContext.Reset();
        (await DataContext.Metadatas.AnyAsync(m => m.Id == replayer.Id)).Should().BeFalse();
        (await DataContext.Metadatas.AnyAsync(m => m.Id == requeueOfReplay.Id)).Should().BeFalse();
        (await DataContext.Metadatas.AnyAsync(m => m.Id == source.Id))
            .Should()
            .BeFalse("the run replayed goes in the same sweep as the replays of it");
    }

    [Test]
    public async Task Run_KeepsAnExpiredReplayWhileTheRunItReplayedIsKept()
    {
        // The run replayed is not expired (another train's retention, or a clock that disagreed),
        // so the replay of it stays too.
        var source = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Failed,
            startTime: DateTime.UtcNow
        );
        var replayer = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Failed,
            startTime: DateTime.UtcNow.AddHours(-2)
        );
        var other = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Completed,
            startTime: DateTime.UtcNow.AddHours(-2)
        );
        await LinkReplay(replayer.Id, source.Id);

        await _train.Run(new MetadataCleanupRequest());

        DataContext.Reset();
        (await DataContext.Metadatas.AnyAsync(m => m.Id == replayer.Id))
            .Should()
            .BeTrue(
                "a replay is never deleted while the run it replays is kept. See "
                    + "docs/adr/0017-a-manifests-retry-replays-the-decisions-of-the-run-it-retries.md"
            );
        (await DataContext.Metadatas.AnyAsync(m => m.Id == source.Id)).Should().BeTrue();
        (await DataContext.Metadatas.AnyAsync(m => m.Id == other.Id)).Should().BeFalse();
    }

    [Test]
    public async Task Delete_LinkedOnEveryAttempt_LeavesTheBatchAndLogsIt()
    {
        var runs = new List<Metadata>();
        for (var i = 0; i < 4; i++)
            runs.Add(
                await CreateAndSaveMetadata(
                    name: typeof(ManifestManagerTrain).FullName!,
                    state: TrainState.Failed,
                    startTime: DateTime.UtcNow.AddHours(-2)
                )
            );
        var logger = new WarningLogger();
        var linked = 0;

        var deleted =
            await Trax.Scheduler.Trains.MetadataCleanup.Junctions.DeleteExpiredMetadataJunction.DeleteUnreferencedAsync(
                DataContext,
                runs.Select(r => r.Id).ToList(),
                CancellationToken.None,
                beforeMetadataDelete: async ct =>
                {
                    // Each attempt, a requeue links another run of the batch.
                    using var other = Scope.ServiceProvider.CreateScope();
                    var otherData =
                        other.ServiceProvider.GetRequiredService<Trax.Effect.Data.Services.DataContext.IDataContext>();
                    otherData.WorkQueues.Add(
                        WorkQueue.Create(
                            new CreateWorkQueue
                            {
                                TrainName = typeof(ManifestManagerTrain).FullName!,
                                ReplayDecisionsOf = runs[linked++].Id,
                            }
                        )
                    );
                    await otherData.SaveChanges(ct);
                },
                logger: logger
            );

        DataContext.Reset();
        deleted.Should().Be((0, 0, 0));
        linked.Should().Be(3);
        (await DataContext.Metadatas.CountAsync(m => runs.Select(r => r.Id).Contains(m.Id)))
            .Should()
            .Be(4, "every attempt rolled back");
        logger
            .Warnings.Should()
            .ContainSingle(w => w.Contains("left 2 expired runs for a later sweep"));
    }

    [Test]
    public async Task Run_ABatchLinkedOnEveryAttempt_IsLeftForTheNextSweep()
    {
        // The sweep selects a full batch, and a requeue links another of its runs during each
        // attempt to delete it. The batch left after the last attempt is not selected again in the
        // same sweep: its unlinked run waits for the next one, as the warning says.
        var runs = new List<Metadata>();
        for (var i = 0; i < 4; i++)
            runs.Add(
                await CreateAndSaveMetadata(
                    name: typeof(ManifestManagerTrain).FullName!,
                    state: TrainState.Failed,
                    startTime: DateTime.UtcNow.AddHours(-2)
                )
            );
        DataContext.Reset();

        var cleanup = new MetadataCleanupConfiguration
        {
            RetentionPeriod = TimeSpan.FromMinutes(30),
            DeleteBatchSize = runs.Count,
        };
        var junction =
            new Trax.Scheduler.Trains.MetadataCleanup.Junctions.DeleteExpiredMetadataJunction(
                DataContext,
                new SchedulerConfiguration { MetadataCleanup = cleanup },
                Microsoft
                    .Extensions
                    .Logging
                    .Abstractions
                    .NullLogger<Trax.Scheduler.Trains.MetadataCleanup.Junctions.DeleteExpiredMetadataJunction>
                    .Instance
            );
        var attempts = 0;
        junction.BeforeMetadataDelete = async ct =>
        {
            if (attempts >= runs.Count - 1)
            {
                attempts++;
                return;
            }

            using var other = Scope.ServiceProvider.CreateScope();
            var otherData =
                other.ServiceProvider.GetRequiredService<Trax.Effect.Data.Services.DataContext.IDataContext>();
            otherData.WorkQueues.Add(
                WorkQueue.Create(
                    new CreateWorkQueue
                    {
                        TrainName = typeof(ManifestManagerTrain).FullName!,
                        ReplayDecisionsOf = runs[attempts++].Id,
                    }
                )
            );
            await otherData.SaveChanges(ct);
        };

        await junction.Run(new MetadataCleanupRequest());

        DataContext.Reset();
        attempts
            .Should()
            .Be(
                Trax.Scheduler
                    .Trains
                    .MetadataCleanup
                    .Junctions
                    .DeleteExpiredMetadataJunction
                    .MaxDeleteAttempts,
                "the batch was tried that many times and not selected again"
            );
        (await DataContext.Metadatas.AnyAsync(m => m.Id == runs[3].Id))
            .Should()
            .BeTrue(
                "a batch left after its attempts waits for the next sweep. See "
                    + "docs/adr/0017-a-manifests-retry-replays-the-decisions-of-the-run-it-retries.md"
            );
    }

    private sealed class WarningLogger : ILogger
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        )
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task Delete_KeepsARunLinkedAfterItWasSelected(bool byQueuedEntry)
    {
        // Both runs were selected as expired and unreferenced. Between that select and the
        // delete, a retry is queued to replay one, or a run is recorded replaying it. The delete
        // statements repeat the test, so the linked run, its entry and its logs survive
        // (docs/adr/0017-a-manifests-retry-replays-the-decisions-of-the-run-it-retries.md).
        var linked = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Failed,
            startTime: DateTime.UtcNow.AddHours(-2)
        );
        var unlinked = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Failed,
            startTime: DateTime.UtcNow.AddHours(-2)
        );
        var ownEntry = WorkQueue.Create(
            new CreateWorkQueue { TrainName = typeof(ManifestManagerTrain).FullName! }
        );
        ownEntry.Status = WorkQueueStatus.Dispatched;
        ownEntry.MetadataId = linked.Id;
        await DataContext.Track(ownEntry);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        if (byQueuedEntry)
            await QueueReplayOf(linked.Id);
        else
        {
            var replayer = await CreateAndSaveMetadata(
                name: typeof(ManifestManagerTrain).FullName!,
                state: TrainState.InProgress,
                startTime: DateTime.UtcNow
            );
            await LinkReplay(replayer.Id, linked.Id);
        }

        var deleted =
            await Trax.Scheduler.Trains.MetadataCleanup.Junctions.DeleteExpiredMetadataJunction.DeleteUnreferencedAsync(
                DataContext,
                [linked.Id, unlinked.Id],
                CancellationToken.None
            );

        DataContext.Reset();
        deleted.Metadata.Should().Be(1);
        (await DataContext.Metadatas.AnyAsync(m => m.Id == linked.Id))
            .Should()
            .BeTrue(
                "a run something came to replay after the select is kept. See "
                    + "docs/adr/0017-a-manifests-retry-replays-the-decisions-of-the-run-it-retries.md"
            );
        (await DataContext.WorkQueues.AnyAsync(q => q.Id == ownEntry.Id))
            .Should()
            .BeTrue("the kept run keeps the entry a retry compares its input with");
        (await DataContext.Metadatas.AnyAsync(m => m.Id == unlinked.Id)).Should().BeFalse();
    }

    [Test]
    public async Task Delete_LinkedMidBatch_KeepsTheRunWithEverythingItOwns()
    {
        // The batch passes its recheck, then a requeue links one run while its owned rows are
        // being cleared. The whole batch rolls back and is retried without that run, so the run
        // keeps its entry, logs, dead letter link and child
        // (docs/adr/0017-a-manifests-retry-replays-the-decisions-of-the-run-it-retries.md).
        var linked = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Failed,
            startTime: DateTime.UtcNow.AddHours(-2)
        );
        var unlinked = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Failed,
            startTime: DateTime.UtcNow.AddHours(-2)
        );
        var ownEntry = WorkQueue.Create(
            new CreateWorkQueue { TrainName = typeof(ManifestManagerTrain).FullName! }
        );
        ownEntry.Status = WorkQueueStatus.Dispatched;
        ownEntry.MetadataId = linked.Id;
        await DataContext.Track(ownEntry);
        await DataContext.SaveChanges(CancellationToken.None);
        var child = await CreateAndSaveChildMetadata(
            name: "Consumer.Trains.SomeChildTrain",
            state: TrainState.InProgress,
            parentId: linked.Id
        );
        var deadLetter = await CreateAndSaveDeadLetter(await CreateAndSaveManifest());
        await DataContext
            .DeadLetters.Where(d => d.Id == deadLetter.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.RetryMetadataId, linked.Id));
        DataContext.Reset();

        var linkedOnce = false;
        var deleted =
            await Trax.Scheduler.Trains.MetadataCleanup.Junctions.DeleteExpiredMetadataJunction.DeleteUnreferencedAsync(
                DataContext,
                [linked.Id, unlinked.Id],
                CancellationToken.None,
                beforeMetadataDelete: async ct =>
                {
                    // Once, on the first attempt: the seam runs before every attempt.
                    if (linkedOnce)
                        return;
                    linkedOnce = true;

                    // Committed on another connection, as a concurrent requeue would be.
                    using var other = Scope.ServiceProvider.CreateScope();
                    var otherData =
                        other.ServiceProvider.GetRequiredService<Trax.Effect.Data.Services.DataContext.IDataContext>();
                    otherData.WorkQueues.Add(
                        WorkQueue.Create(
                            new CreateWorkQueue
                            {
                                TrainName = typeof(ManifestManagerTrain).FullName!,
                                ReplayDecisionsOf = linked.Id,
                            }
                        )
                    );
                    await otherData.SaveChanges(ct);
                }
            );

        DataContext.Reset();
        const string because =
            "a run linked mid-batch keeps everything it owns. See "
            + "docs/adr/0017-a-manifests-retry-replays-the-decisions-of-the-run-it-retries.md";
        deleted.Metadata.Should().Be(1);
        (await DataContext.Metadatas.AnyAsync(m => m.Id == linked.Id)).Should().BeTrue(because);
        (await DataContext.WorkQueues.AnyAsync(q => q.Id == ownEntry.Id)).Should().BeTrue(because);
        (await DataContext.Metadatas.SingleAsync(m => m.Id == child.Id))
            .ParentId.Should()
            .Be(linked.Id, because);
        (await DataContext.DeadLetters.SingleAsync(d => d.Id == deadLetter.Id))
            .RetryMetadataId.Should()
            .Be(linked.Id, because);
        (await DataContext.Metadatas.AnyAsync(m => m.Id == unlinked.Id)).Should().BeFalse();
    }

    private async Task<WorkQueue> QueueReplayOf(long metadataId)
    {
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = typeof(ManifestManagerTrain).FullName!,
                Input = null,
                InputTypeName = null,
                ReplayDecisionsOf = metadataId,
            }
        );
        await DataContext.Track(entry);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return entry;
    }

    private async Task LinkReplay(long replayerId, long sourceId)
    {
        await DataContext
            .Metadatas.Where(m => m.Id == replayerId)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ReplayDecisionsOf, (long?)sourceId));
        DataContext.Reset();
    }

    #endregion

    #region Edge Cases

    [Test]
    public async Task Run_WithNoExpiredMetadata_CompletesSuccessfully()
    {
        // Act & Assert - Should complete without throwing
        var act = async () => await _train.Run(new MetadataCleanupRequest());
        await act.Should().NotThrowAsync();
    }

    [Test]
    public async Task Run_WithMixOfEligibleAndIneligibleMetadata_DeletesOnlyEligible()
    {
        // Arrange
        var expiredWhitelisted = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Completed,
            startTime: DateTime.UtcNow.AddHours(-2)
        );

        var recentWhitelisted = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Completed,
            startTime: DateTime.UtcNow.AddMinutes(-10)
        );

        var expiredNonWhitelisted = await CreateAndSaveMetadata(
            name: "SomeOtherTrain",
            state: TrainState.Completed,
            startTime: DateTime.UtcNow.AddHours(-2)
        );

        var expiredPending = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Pending,
            startTime: DateTime.UtcNow.AddHours(-2)
        );

        // Act
        await _train.Run(new MetadataCleanupRequest());

        // Assert
        DataContext.Reset();

        var deletedCheck = await DataContext
            .Metadatas.Where(m => m.Id == expiredWhitelisted.Id)
            .FirstOrDefaultAsync();
        deletedCheck.Should().BeNull("expired whitelisted completed metadata should be deleted");

        var recentCheck = await DataContext
            .Metadatas.Where(m => m.Id == recentWhitelisted.Id)
            .FirstOrDefaultAsync();
        recentCheck.Should().NotBeNull("recent metadata should survive");

        var nonWhitelistedCheck = await DataContext
            .Metadatas.Where(m => m.Id == expiredNonWhitelisted.Id)
            .FirstOrDefaultAsync();
        nonWhitelistedCheck.Should().NotBeNull("non-whitelisted metadata should survive");

        var pendingCheck = await DataContext
            .Metadatas.Where(m => m.Id == expiredPending.Id)
            .FirstOrDefaultAsync();
        pendingCheck.Should().NotBeNull("pending metadata should survive");
    }

    #endregion

    #region Batched Deletion Tests

    [Test]
    public async Task Run_WithMoreExpiredThanBatchSize_DeletesAllInMultipleBatches()
    {
        // Arrange - Set batch size to 2 and create 5 expired metadata rows
        _config.MetadataCleanup!.DeleteBatchSize = 2;

        for (var i = 0; i < 5; i++)
        {
            await CreateAndSaveMetadata(
                name: typeof(ManifestManagerTrain).FullName!,
                state: TrainState.Completed,
                startTime: DateTime.UtcNow.AddHours(-2)
            );
        }

        // Act
        await _train.Run(new MetadataCleanupRequest());

        // Assert - All 5 should be deleted (3 batches: 2 + 2 + 1)
        DataContext.Reset();
        var remaining = await DataContext
            .Metadatas.Where(m => m.Name == typeof(ManifestManagerTrain).FullName!)
            .CountAsync();

        remaining.Should().Be(0, "all expired metadata should be deleted across multiple batches");
    }

    [Test]
    public async Task Run_WithBatchSizeNull_DeletesAllInCappedBatches()
    {
        // Arrange - Disable batching
        _config.MetadataCleanup!.DeleteBatchSize = null;

        for (var i = 0; i < 5; i++)
        {
            await CreateAndSaveMetadata(
                name: typeof(ManifestManagerTrain).FullName!,
                state: TrainState.Completed,
                startTime: DateTime.UtcNow.AddHours(-2)
            );
        }

        // Act
        await _train.Run(new MetadataCleanupRequest());

        // Assert
        DataContext.Reset();
        var remaining = await DataContext
            .Metadatas.Where(m => m.Name == typeof(ManifestManagerTrain).FullName!)
            .CountAsync();

        remaining.Should().Be(0, "a null batch size sweeps everything, a capped batch at a time");
    }

    [Test]
    public async Task Run_BatchedDeletion_DeletesAssociatedLogsAndWorkQueues()
    {
        // Arrange - Small batch size with associated FK rows
        _config.MetadataCleanup!.DeleteBatchSize = 1;

        var metadata = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Completed,
            startTime: DateTime.UtcNow.AddHours(-2)
        );

        // Create associated log
        var log = Log.Create(
            new CreateLog
            {
                Level = LogLevel.Information,
                Message = "Test log entry",
                CategoryName = "TestCategory",
                EventId = 1,
            }
        );
        await DataContext.Logs.AddAsync(log);
        await DataContext.SaveChanges(CancellationToken.None);
        var logId = log.Id;
        await DataContext
            .Logs.Where(l => l.Id == logId)
            .ExecuteUpdateAsync(setters => setters.SetProperty(l => l.MetadataId, metadata.Id));

        // Create associated work queue entry
        var workQueueEntry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = typeof(ManifestManagerTrain).FullName!,
                Input = null,
                InputTypeName = null,
            }
        );
        await DataContext.Track(workQueueEntry);
        await DataContext.SaveChanges(CancellationToken.None);
        var entryId = workQueueEntry.Id;
        await DataContext
            .WorkQueues.Where(wq => wq.Id == entryId)
            .ExecuteUpdateAsync(setters =>
                setters
                    .SetProperty(wq => wq.MetadataId, metadata.Id)
                    .SetProperty(wq => wq.Status, WorkQueueStatus.Dispatched)
                    .SetProperty(wq => wq.DispatchedAt, DateTime.UtcNow)
            );

        DataContext.Reset();

        // Act
        await _train.Run(new MetadataCleanupRequest());

        // Assert
        DataContext.Reset();
        var remainingMetadata = await DataContext
            .Metadatas.Where(m => m.Id == metadata.Id)
            .FirstOrDefaultAsync();
        var remainingLog = await DataContext.Logs.Where(l => l.Id == logId).FirstOrDefaultAsync();
        var remainingEntry = await DataContext
            .WorkQueues.Where(wq => wq.Id == entryId)
            .FirstOrDefaultAsync();

        remainingMetadata.Should().BeNull("metadata should be deleted in batched mode");
        remainingLog.Should().BeNull("associated logs should be deleted in batched mode");
        remainingEntry
            .Should()
            .BeNull("associated work queue entries should be deleted in batched mode");
    }

    [Test]
    public async Task Run_BatchedDeletion_DoesNotDeleteNonExpiredMetadata()
    {
        // Arrange - Mix of expired and non-expired, with small batch size
        _config.MetadataCleanup!.DeleteBatchSize = 1;

        var expired = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Completed,
            startTime: DateTime.UtcNow.AddHours(-2)
        );

        var recent = await CreateAndSaveMetadata(
            name: typeof(ManifestManagerTrain).FullName!,
            state: TrainState.Completed,
            startTime: DateTime.UtcNow.AddMinutes(-10)
        );

        // Act
        await _train.Run(new MetadataCleanupRequest());

        // Assert
        DataContext.Reset();
        var expiredCheck = await DataContext
            .Metadatas.Where(m => m.Id == expired.Id)
            .FirstOrDefaultAsync();
        var recentCheck = await DataContext
            .Metadatas.Where(m => m.Id == recent.Id)
            .FirstOrDefaultAsync();

        expiredCheck.Should().BeNull("expired metadata should be deleted");
        recentCheck.Should().NotBeNull("recent metadata should survive batched cleanup");
    }

    #endregion

    #region Configuration Tests

    [TestCase(null, 10_000)]
    [TestCase(1, 1)]
    [TestCase(1000, 1000)]
    public void BatchSize_NullIsCapped_SoOneTransactionNeverHoldsTheWholeBacklog(
        int? configured,
        int used
    ) =>
        Trax
            .Scheduler.Trains.MetadataCleanup.Junctions.DeleteExpiredMetadataJunction.BatchSize(
                configured
            )
            .Should()
            .Be(used);

    [Test]
    public void DefaultDeleteBatchSize_IsOneThousand()
    {
        _config
            .MetadataCleanup!.DeleteBatchSize.Should()
            .Be(1000, "default delete batch size should be 1000");
    }

    [Test]
    public void AddTrainType_Generic_AddsTypeName()
    {
        var config = new MetadataCleanupConfiguration();
        config.AddTrainType<ManifestManagerTrain>();

        config.TrainTypeWhitelist.Should().Contain(typeof(ManifestManagerTrain).FullName!);
    }

    [Test]
    public void AddTrainType_String_AddsName()
    {
        var config = new MetadataCleanupConfiguration();
        config.AddTrainType("CustomTrain");

        config.TrainTypeWhitelist.Should().Contain("CustomTrain");
    }

    [Test]
    public void AddTrainType_CanAppendMultipleTypes()
    {
        var config = new MetadataCleanupConfiguration();
        config.AddTrainType<ManifestManagerTrain>();
        config.AddTrainType<MetadataCleanupTrain>();
        config.AddTrainType("ThirdTrain");

        config
            .TrainTypeWhitelist.Should()
            .HaveCount(3)
            .And.Contain(typeof(ManifestManagerTrain).FullName!)
            .And.Contain(typeof(MetadataCleanupTrain).FullName!)
            .And.Contain("ThirdTrain");
    }

    #endregion

    #region Helper Methods

    private async Task<Metadata> CreateAndSaveMetadata(
        string name,
        TrainState state,
        DateTime startTime
    )
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = name,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );

        metadata.TrainState = state;
        metadata.StartTime = startTime;

        if (state is TrainState.Completed or TrainState.Failed)
            metadata.EndTime = startTime.AddSeconds(1);

        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        return metadata;
    }

    private async Task<Metadata> CreateAndSaveChildMetadata(
        string name,
        TrainState state,
        long parentId
    )
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = name,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
                ParentId = parentId,
            }
        );

        metadata.TrainState = state;
        metadata.StartTime = DateTime.UtcNow;

        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        return metadata;
    }

    private async Task<Manifest> CreateAndSaveManifest()
    {
        var group = await TestSetup.CreateAndSaveManifestGroup(
            DataContext,
            name: $"group-{Guid.NewGuid():N}"
        );

        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = true,
                ScheduleType = ScheduleType.Interval,
                IntervalSeconds = 60,
                MaxRetries = 3,
                Properties = new SchedulerTestInput { Value = "cleanup-fk-test" },
            }
        );

        manifest.ManifestGroupId = group.Id;

        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        return manifest;
    }

    private async Task<DeadLetter> CreateAndSaveDeadLetter(Manifest manifest)
    {
        var reloadedManifest = await DataContext.Manifests.FirstAsync(m => m.Id == manifest.Id);

        var deadLetter = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = reloadedManifest,
                Reason = "Test dead letter",
                RetryCount = 3,
            }
        );

        await DataContext.Track(deadLetter);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        return deadLetter;
    }

    #endregion
}
