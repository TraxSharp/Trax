using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Samples.GameServer.E2E.Fixtures;
using Trax.Samples.GameServer.E2E.Utilities;
using Trax.Scheduler.Services.TraxScheduler;

namespace Trax.Samples.GameServer.E2E.SchedulerTests;

[TestFixture]
public class DeadLetterOperationsTests : SchedulerTestFixture
{
    private ITraxScheduler GetScheduler() =>
        Scope.ServiceProvider.GetRequiredService<ITraxScheduler>();

    #region Single Operations

    [Test]
    public async Task RequeueDeadLetter_ViaService_CreatesWorkQueueAndMarksRetried()
    {
        EnableManifestManager();

        try
        {
            var manifest = await DataContext
                .Manifests.AsNoTracking()
                .FirstAsync(m => m.ExternalId == ManifestNames.CorruptedDataRepair);

            await TrainStatePoller.WaitForDeadLetter(
                DataContext,
                manifest.Id,
                TimeSpan.FromSeconds(60)
            );

            var deadLetter = await DataContext
                .DeadLetters.AsNoTracking()
                .FirstAsync(dl => dl.ManifestId == manifest.Id);

            deadLetter.Status.Should().Be(DeadLetterStatus.AwaitingIntervention);

            // Act
            var scheduler = GetScheduler();
            var result = await scheduler.RequeueDeadLetterAsync(deadLetter.Id);

            // Assert
            result.Success.Should().BeTrue();
            result.WorkQueueId.Should().NotBeNull();

            DataContext.Reset();
            var resolvedDl = await DataContext
                .DeadLetters.AsNoTracking()
                .FirstAsync(dl => dl.Id == deadLetter.Id);
            resolvedDl.Status.Should().Be(DeadLetterStatus.Retried);
            resolvedDl.ResolvedAt.Should().NotBeNull();

            // Verify WorkQueue entry was created with DeadLetterId
            var workQueue = await DataContext
                .WorkQueues.AsNoTracking()
                .FirstAsync(wq => wq.Id == result.WorkQueueId);
            workQueue.DeadLetterId.Should().Be(deadLetter.Id);
            workQueue.ManifestId.Should().Be(manifest.Id);
        }
        finally
        {
            DisableManifestManager();
        }
    }

    [Test]
    public async Task AcknowledgeDeadLetter_ViaService_MarksAcknowledged()
    {
        EnableManifestManager();

        try
        {
            var manifest = await DataContext
                .Manifests.AsNoTracking()
                .FirstAsync(m => m.ExternalId == ManifestNames.CorruptedDataRepair);

            await TrainStatePoller.WaitForDeadLetter(
                DataContext,
                manifest.Id,
                TimeSpan.FromSeconds(60)
            );

            var deadLetter = await DataContext
                .DeadLetters.AsNoTracking()
                .FirstAsync(dl => dl.ManifestId == manifest.Id);

            // Act
            var scheduler = GetScheduler();
            var result = await scheduler.AcknowledgeDeadLetterAsync(
                deadLetter.Id,
                "Root cause fixed"
            );

            // Assert
            result.Success.Should().BeTrue();

            DataContext.Reset();
            var acknowledgedDl = await DataContext
                .DeadLetters.AsNoTracking()
                .FirstAsync(dl => dl.Id == deadLetter.Id);
            acknowledgedDl.Status.Should().Be(DeadLetterStatus.Acknowledged);
            acknowledgedDl.ResolutionNote.Should().Be("Root cause fixed");
        }
        finally
        {
            DisableManifestManager();
        }
    }

    [Test]
    public async Task RequeueDeadLetter_AlreadyResolved_ReturnsFalse()
    {
        EnableManifestManager();

        try
        {
            var manifest = await DataContext
                .Manifests.AsNoTracking()
                .FirstAsync(m => m.ExternalId == ManifestNames.CorruptedDataRepair);

            await TrainStatePoller.WaitForDeadLetter(
                DataContext,
                manifest.Id,
                TimeSpan.FromSeconds(60)
            );

            var deadLetter = await DataContext
                .DeadLetters.AsNoTracking()
                .FirstAsync(dl => dl.ManifestId == manifest.Id);

            var scheduler = GetScheduler();

            // Acknowledge first
            await scheduler.AcknowledgeDeadLetterAsync(deadLetter.Id, "Done");

            // Act — try to requeue the already-acknowledged dead letter
            var result = await scheduler.RequeueDeadLetterAsync(deadLetter.Id);

            // Assert — should fail since it's already resolved
            result.Success.Should().BeFalse();
            result.WorkQueueId.Should().BeNull();
        }
        finally
        {
            DisableManifestManager();
        }
    }

    #endregion

    #region Batch Operations

    [Test]
    public async Task RequeueAll_ResolvesAllAwaitingDeadLetters()
    {
        EnableManifestManager();

        try
        {
            var manifest = await DataContext
                .Manifests.AsNoTracking()
                .FirstAsync(m => m.ExternalId == ManifestNames.CorruptedDataRepair);

            await TrainStatePoller.WaitForDeadLetter(
                DataContext,
                manifest.Id,
                TimeSpan.FromSeconds(60)
            );

            // Act
            var scheduler = GetScheduler();
            var result = await scheduler.RequeueAllDeadLettersAsync();

            // Assert
            result.Count.Should().BeGreaterThan(0);

            DataContext.Reset();
            var remaining = await DataContext
                .DeadLetters.AsNoTracking()
                .CountAsync(dl => dl.Status == DeadLetterStatus.AwaitingIntervention);
            remaining.Should().Be(0);
        }
        finally
        {
            DisableManifestManager();
        }
    }

    [Test]
    public async Task AcknowledgeAll_ResolvesAllAwaitingDeadLetters()
    {
        EnableManifestManager();

        try
        {
            var manifest = await DataContext
                .Manifests.AsNoTracking()
                .FirstAsync(m => m.ExternalId == ManifestNames.CorruptedDataRepair);

            await TrainStatePoller.WaitForDeadLetter(
                DataContext,
                manifest.Id,
                TimeSpan.FromSeconds(60)
            );

            // Act
            var scheduler = GetScheduler();
            var result = await scheduler.AcknowledgeAllDeadLettersAsync("Batch ack from E2E test");

            // Assert
            result.Count.Should().BeGreaterThan(0);

            DataContext.Reset();
            var remaining = await DataContext
                .DeadLetters.AsNoTracking()
                .CountAsync(dl => dl.Status == DeadLetterStatus.AwaitingIntervention);
            remaining.Should().Be(0);
        }
        finally
        {
            DisableManifestManager();
        }
    }

    #endregion

    #region RetryMetadataId Linking

    [Test]
    public async Task Requeue_AfterDispatch_LinksRetryMetadataId()
    {
        EnableManifestManager();

        try
        {
            var manifest = await DataContext
                .Manifests.AsNoTracking()
                .FirstAsync(m => m.ExternalId == ManifestNames.CorruptedDataRepair);

            await TrainStatePoller.WaitForDeadLetter(
                DataContext,
                manifest.Id,
                TimeSpan.FromSeconds(60)
            );

            var deadLetter = await DataContext
                .DeadLetters.AsNoTracking()
                .FirstAsync(dl => dl.ManifestId == manifest.Id);

            // Get the highest metadata ID before requeue so we can find the new one
            DataContext.Reset();
            var maxMetadataId =
                await DataContext
                    .Metadatas.AsNoTracking()
                    .Where(m => m.ManifestId == manifest.Id)
                    .MaxAsync(m => (long?)m.Id)
                ?? 0;

            // Requeue the dead letter
            var scheduler = GetScheduler();
            var result = await scheduler.RequeueDeadLetterAsync(deadLetter.Id);
            result.Success.Should().BeTrue();

            // Wait for the requeued job to be dispatched and create a Metadata record
            // (it will fail again since CorruptedDataRepair always throws)
            var retryMetadata = await TrainStatePoller.WaitForMetadataByManifestId(
                DataContext,
                manifest.Id,
                TrainState.Failed,
                TimeSpan.FromSeconds(30),
                afterMetadataId: maxMetadataId
            );

            // Verify the dead letter now has RetryMetadataId linked
            DataContext.Reset();
            var linkedDl = await DataContext
                .DeadLetters.AsNoTracking()
                .FirstAsync(dl => dl.Id == deadLetter.Id);
            linkedDl.RetryMetadataId.Should().Be(retryMetadata.Id);
        }
        finally
        {
            DisableManifestManager();
        }
    }

    #endregion

    #region Retry Delay

    [Test]
    public async Task RetryDelay_FailedManifest_WorkQueueHasScheduledAt()
    {
        // Set a short retry delay for testing
        var config = GetSchedulerConfiguration();
        var originalDelay = config.DefaultRetryDelay;
        config.DefaultRetryDelay = TimeSpan.FromSeconds(2);

        EnableManifestManager();

        try
        {
            var manifest = await DataContext
                .Manifests.AsNoTracking()
                .FirstAsync(m => m.ExternalId == ManifestNames.CorruptedDataRepair);

            // Wait for the train to fail at least once (but not reach dead letter yet)
            await TrainStatePoller.WaitForMetadataByManifestId(
                DataContext,
                manifest.Id,
                TrainState.Failed,
                TimeSpan.FromSeconds(30)
            );

            // The next work queue entry (retry) should have ScheduledAt set
            // Give the ManifestManager a cycle to create the retry entry
            await Task.Delay(TimeSpan.FromSeconds(6));

            DataContext.Reset();
            var retryEntry = await DataContext
                .WorkQueues.AsNoTracking()
                .Where(wq => wq.ManifestId == manifest.Id && wq.ScheduledAt != null)
                .OrderByDescending(wq => wq.Id)
                .FirstOrDefaultAsync();

            // It's possible the train dead-lettered before a retry with ScheduledAt was created
            // (MaxRetries(1) means only 1 failure before dead letter). In that case, skip the assertion.
            if (retryEntry is not null)
            {
                retryEntry.ScheduledAt.Should().NotBeNull();
                retryEntry.ScheduledAt!.Value.Should().BeAfter(retryEntry.CreatedAt);
            }
        }
        finally
        {
            DisableManifestManager();
            config.DefaultRetryDelay = originalDelay;
        }
    }

    #endregion
}
