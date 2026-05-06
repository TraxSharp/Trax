using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.DeadLetter.DTOs;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.JobDispatcher;
using Every = Trax.Scheduler.Services.Scheduling.Every;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Targets uncovered branches in DispatchJobsJunction (dead-letter retry-link, null-input)
/// and LoadQueuedJobsJunction (LoadAllQueued path, used when MaxQueuedJobsPerCycle is null).
/// </summary>
[TestFixture]
public class DispatchAndLoadCoverageTests
{
    #region DispatchJobsJunction — dead-letter retry-metadata link

    [Test]
    public async Task Dispatch_WhenWorkQueueHasDeadLetterId_LinksRetryMetadataOnDeadLetter()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain>(
                "dl-retry",
                new SchedulerTestInput { Value = "x" },
                Every.Minutes(5)
            )
        );
        await fx.MaterializePendingManifestsAsync();

        var manifest = await fx
            .DataContext.Manifests.Include(m => m.ManifestGroup)
            .FirstAsync(m => m.ExternalId == "dl-retry");

        var deadLetter = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = manifest,
                Reason = "test",
                RetryCount = 1,
            }
        );
        await fx.DataContext.Track(deadLetter);
        await fx.DataContext.SaveChanges(default);
        fx.DataContext.Reset();

        // Requeue creates a WorkQueue with DeadLetterId set
        var requeueResult = await fx.Scheduler.RequeueDeadLetterAsync(deadLetter.Id);
        requeueResult.Success.Should().BeTrue();

        // Dispatch — this exercises the LinkRetryMetadata branch
        await fx.RunJobDispatcherAsync();

        var reloadedDl = await fx
            .DataContext.DeadLetters.AsNoTracking()
            .FirstAsync(d => d.Id == deadLetter.Id);
        reloadedDl
            .RetryMetadataId.Should()
            .NotBeNull("dispatched retry must be linked to dead letter");

        var retryMetadata = await fx
            .DataContext.Metadatas.AsNoTracking()
            .FirstAsync(m => m.Id == reloadedDl.RetryMetadataId);
        retryMetadata.ManifestId.Should().Be(manifest.Id);
    }

    #endregion

    #region LoadQueuedJobsJunction — LoadAllQueued path (no per-cycle limit)

    [Test]
    public async Task Dispatch_WhenMaxQueuedJobsPerCycleIsNull_LoadsAllQueuedEntries()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
        {
            s.MaxQueuedJobsPerCycle(null);
            s.Schedule<ISchedulerTestTrain>(
                "load-all-1",
                new SchedulerTestInput(),
                Every.Minutes(5)
            );
            s.Schedule<ISchedulerTestTrain>(
                "load-all-2",
                new SchedulerTestInput(),
                Every.Minutes(5)
            );
        });
        await fx.MaterializePendingManifestsAsync();

        // Force two queued entries (one per manifest) by triggering them directly.
        await fx.Scheduler.TriggerAsync("load-all-1");
        await fx.Scheduler.TriggerAsync("load-all-2");

        await fx.RunJobDispatcherAsync();

        var dispatched = await fx
            .DataContext.WorkQueues.AsNoTracking()
            .Where(w => w.Status == WorkQueueStatus.Dispatched)
            .ToListAsync();
        dispatched
            .Count.Should()
            .BeGreaterThanOrEqualTo(
                2,
                "all queued entries must be dispatched in the no-limit path"
            );
    }

    #endregion
}
