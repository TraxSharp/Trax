using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Trax.Core.Functional;
using Trax.Effect.Enums;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.DeadLetter.DTOs;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Every = Trax.Scheduler.Services.Scheduling.Every;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// E2E coverage of TraxScheduler dead-letter operations: RequeueDeadLetterAsync,
/// AcknowledgeDeadLetterAsync, the batch and "all" variants. Each test seeds a manifest +
/// dead letter directly into Postgres, then exercises the scheduler API and asserts on the
/// resulting Status / WorkQueue rows.
/// </summary>
[TestFixture]
public class SchedulerDeadLetterTests
{
    private static async Task<DeadLetter> SeedDeadLetterAsync(
        SchedulerE2EFixture fx,
        string externalId
    )
    {
        var manifest = await fx
            .DataContext.Manifests.Include(m => m.ManifestGroup)
            .FirstAsync(m => m.ExternalId == externalId);
        var dl = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = manifest,
                Reason = "test",
                RetryCount = 3,
            }
        );
        await fx.DataContext.Track(dl);
        await fx.DataContext.SaveChanges(default);
        fx.DataContext.Reset();
        return dl;
    }

    private static async Task<SchedulerE2EFixture> CreateWithManifestAsync(string externalId)
    {
        var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain>(externalId, new SchedulerTestInput(), Every.Minutes(5))
        );
        await fx.MaterializePendingManifestsAsync();
        return fx;
    }

    [Test]
    public async Task RequeueDeadLetterAsync_ValidId_RequeuesAndMarksRequeued()
    {
        await using var fx = await CreateWithManifestAsync("dl-1");
        var dl = await SeedDeadLetterAsync(fx, "dl-1");

        var result = await fx.Scheduler.RequeueDeadLetterAsync(dl.Id);

        result.Success.Should().BeTrue();
        result.WorkQueueId.Should().NotBeNull();

        var reloaded = await fx
            .DataContext.DeadLetters.AsNoTracking()
            .FirstAsync(d => d.Id == dl.Id);
        reloaded.Status.Should().Be(DeadLetterStatus.Retried);
        reloaded.ResolutionNote.Should().Contain("Re-queued");
    }

    [Test]
    public async Task RequeueDeadLetterAsync_MissingId_ReturnsFailure()
    {
        await using var fx = await CreateWithManifestAsync("dl-2");

        var result = await fx.Scheduler.RequeueDeadLetterAsync(999_999);

        result.Success.Should().BeFalse();
        result.WorkQueueId.Should().BeNull();
    }

    [Test]
    public async Task AcknowledgeDeadLetterAsync_ValidId_MarksAcknowledged()
    {
        await using var fx = await CreateWithManifestAsync("dl-3");
        var dl = await SeedDeadLetterAsync(fx, "dl-3");

        var result = await fx.Scheduler.AcknowledgeDeadLetterAsync(dl.Id, "intentional");

        result.Success.Should().BeTrue();
        var reloaded = await fx
            .DataContext.DeadLetters.AsNoTracking()
            .FirstAsync(d => d.Id == dl.Id);
        reloaded.Status.Should().Be(DeadLetterStatus.Acknowledged);
        reloaded.ResolutionNote.Should().Be("intentional");
    }

    [Test]
    public async Task AcknowledgeDeadLetterAsync_MissingId_ReturnsFailure()
    {
        await using var fx = await CreateWithManifestAsync("dl-4");

        var result = await fx.Scheduler.AcknowledgeDeadLetterAsync(999_999, "n/a");

        result.Success.Should().BeFalse();
    }

    [Test]
    public async Task RequeueDeadLettersAsync_BatchAcrossManifests_RequeuesEach()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain>("dl-5a", new SchedulerTestInput(), Every.Minutes(5))
                .Include<ISchedulerTestTrain>("dl-5b", new SchedulerTestInput())
        );
        await fx.MaterializePendingManifestsAsync();
        var d1 = await SeedDeadLetterAsync(fx, "dl-5a");
        var d2 = await SeedDeadLetterAsync(fx, "dl-5b");

        var result = await fx.Scheduler.RequeueDeadLettersAsync(new[] { d1.Id, d2.Id });

        result.Count.Should().Be(2);
        var reloaded = await fx
            .DataContext.DeadLetters.AsNoTracking()
            .Where(d => d.Id == d1.Id || d.Id == d2.Id)
            .ToListAsync();
        reloaded.Should().AllSatisfy(d => d.Status.Should().Be(DeadLetterStatus.Retried));
    }

    [Test]
    public async Task AcknowledgeDeadLettersAsync_BatchOfIds_AcknowledgesEach()
    {
        await using var fx = await CreateWithManifestAsync("dl-6");
        var d1 = await SeedDeadLetterAsync(fx, "dl-6");
        var d2 = await SeedDeadLetterAsync(fx, "dl-6");

        var result = await fx.Scheduler.AcknowledgeDeadLettersAsync(
            new[] { d1.Id, d2.Id },
            "batch-ack"
        );

        result.Count.Should().Be(2);
        var reloaded = await fx
            .DataContext.DeadLetters.AsNoTracking()
            .Where(d => d.Id == d1.Id || d.Id == d2.Id)
            .ToListAsync();
        reloaded.Should().AllSatisfy(d => d.Status.Should().Be(DeadLetterStatus.Acknowledged));
    }

    [Test]
    public async Task RequeueAllDeadLettersAsync_RequeuesEveryAwaitingIntervention()
    {
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
            s.Schedule<ISchedulerTestTrain>("dl-7a", new SchedulerTestInput(), Every.Minutes(5))
                .Include<ISchedulerTestTrain>("dl-7b", new SchedulerTestInput())
                .Include<ISchedulerTestTrain>("dl-7c", new SchedulerTestInput())
        );
        await fx.MaterializePendingManifestsAsync();
        await SeedDeadLetterAsync(fx, "dl-7a");
        await SeedDeadLetterAsync(fx, "dl-7b");
        await SeedDeadLetterAsync(fx, "dl-7c");

        var result = await fx.Scheduler.RequeueAllDeadLettersAsync();

        result.Count.Should().Be(3);
    }

    [Test]
    public async Task DeadLetterCleanup_ResolvedAndExpired_AreDeleted()
    {
        await using var fx = await CreateWithManifestAsync("dl-cleanup");
        var dl = await SeedDeadLetterAsync(fx, "dl-cleanup");

        // Acknowledge so it has a ResolvedAt timestamp
        await fx.Scheduler.AcknowledgeDeadLetterAsync(dl.Id, "old");

        // Backdate ResolvedAt so it falls outside the retention window (default 30 days)
        await fx
            .DataContext.DeadLetters.Where(d => d.Id == dl.Id)
            .ExecuteUpdateAsync(s =>
                s.SetProperty(d => d.ResolvedAt, DateTime.UtcNow.AddDays(-90))
            );

        await fx.RunDeadLetterCleanupAsync();

        var exists = await fx.DataContext.DeadLetters.AsNoTracking().AnyAsync(d => d.Id == dl.Id);
        exists.Should().BeFalse();
    }

    [Test]
    public async Task DeadLetterCleanup_RequeuedEntryStillQueued_KeepsTheDeadLetterAndItsRetry()
    {
        await using var fx = await CreateWithManifestAsync("dl-queued-retry");
        var dl = await SeedDeadLetterAsync(fx, "dl-queued-retry");

        // The operator retries it, but the dispatcher is paused, so the retry waits in the queue
        // past the retention period.
        (await fx.Scheduler.RequeueDeadLetterAsync(dl.Id))
            .Success.Should()
            .BeTrue();
        await fx
            .DataContext.DeadLetters.Where(d => d.Id == dl.Id)
            .ExecuteUpdateAsync(s =>
                s.SetProperty(d => d.ResolvedAt, DateTime.UtcNow.AddDays(-90))
            );

        await fx.RunDeadLetterCleanupAsync();

        (
            await fx
                .DataContext.WorkQueues.AsNoTracking()
                .AnyAsync(w => w.DeadLetterId == dl.Id && w.Status == WorkQueueStatus.Queued)
        )
            .Should()
            .BeTrue("the purge must not drop a retry that has not run yet");
        (await fx.DataContext.DeadLetters.AsNoTracking().AnyAsync(d => d.Id == dl.Id))
            .Should()
            .BeTrue("it is purged once its retry has left the queue");
    }

    [Test]
    public async Task DeadLetterCleanup_AwaitingIntervention_NotDeleted()
    {
        await using var fx = await CreateWithManifestAsync("dl-keep");
        var dl = await SeedDeadLetterAsync(fx, "dl-keep");

        await fx.RunDeadLetterCleanupAsync();

        var exists = await fx.DataContext.DeadLetters.AsNoTracking().AnyAsync(d => d.Id == dl.Id);
        exists.Should().BeTrue();
    }

    [Test]
    public async Task DeadLetterCleanup_RecentlyResolved_NotDeleted()
    {
        await using var fx = await CreateWithManifestAsync("dl-recent");
        var dl = await SeedDeadLetterAsync(fx, "dl-recent");

        await fx.Scheduler.AcknowledgeDeadLetterAsync(dl.Id, "recent");

        await fx.RunDeadLetterCleanupAsync();

        var exists = await fx.DataContext.DeadLetters.AsNoTracking().AnyAsync(d => d.Id == dl.Id);
        exists.Should().BeTrue();
    }

    [Test]
    public async Task AcknowledgeAllDeadLettersAsync_AcknowledgesEveryAwaitingIntervention()
    {
        await using var fx = await CreateWithManifestAsync("dl-8");
        await SeedDeadLetterAsync(fx, "dl-8");
        await SeedDeadLetterAsync(fx, "dl-8");

        var result = await fx.Scheduler.AcknowledgeAllDeadLettersAsync("clearing");

        result.Count.Should().Be(2);
    }

    #region One queued entry per manifest (the partial unique index on work_queue)

    private static async Task SeedQueuedEntryAsync(SchedulerE2EFixture fx, string externalId)
    {
        var manifest = await fx.DataContext.Manifests.FirstAsync(m => m.ExternalId == externalId);
        var entry = Trax.Effect.Models.WorkQueue.WorkQueue.Create(
            new Trax.Effect.Models.WorkQueue.DTOs.CreateWorkQueue
            {
                TrainName = manifest.Name,
                Input = manifest.Properties,
                InputTypeName = manifest.PropertyTypeName,
                ManifestId = manifest.Id,
            }
        );
        await fx.DataContext.Track(entry);
        await fx.DataContext.SaveChanges(default);
        fx.DataContext.Reset();
    }

    private static async Task<int> QueuedFor(SchedulerE2EFixture fx, string externalId) =>
        await fx
            .DataContext.WorkQueues.AsNoTracking()
            .CountAsync(q =>
                q.Manifest!.ExternalId == externalId && q.Status == WorkQueueStatus.Queued
            );

    private static Task<SchedulerE2EFixture> CreateWithTwoManifestsAsync(string a, string b) =>
        SchedulerE2EFixture
            .CreateAsync(s =>
                s.Schedule<ISchedulerTestTrain>(a, new SchedulerTestInput(), Every.Minutes(5))
                    .Include<ISchedulerTestTrain>(b, new SchedulerTestInput())
            )
            .ContinueWith(async t =>
            {
                await t.Result.MaterializePendingManifestsAsync();
                return t.Result;
            })
            .Unwrap();

    [Test]
    public async Task RequeueAllDeadLettersAsync_ManifestAlreadyQueued_IsSkippedAndTheRestSucceed()
    {
        await using var fx = await CreateWithTwoManifestsAsync("dl-q1a", "dl-q1b");
        await SeedQueuedEntryAsync(fx, "dl-q1a");
        var blocked = await SeedDeadLetterAsync(fx, "dl-q1a");
        var free = await SeedDeadLetterAsync(fx, "dl-q1b");

        var result = await fx.Scheduler.RequeueAllDeadLettersAsync();

        result.Count.Should().Be(1, "only the dead letter whose manifest was free is requeued");
        result.Message.Should().Contain("1 skipped");
        (await QueuedFor(fx, "dl-q1a")).Should().Be(1, "the unique index allows one queued entry");
        (await QueuedFor(fx, "dl-q1b")).Should().Be(1);
        var statuses = await fx
            .DataContext.DeadLetters.AsNoTracking()
            .ToDictionaryAsync(d => d.Id, d => d.Status);
        statuses[blocked.Id]
            .Should()
            .Be(
                DeadLetterStatus.AwaitingIntervention,
                "a skipped dead letter stays for the operator"
            );
        statuses[free.Id].Should().Be(DeadLetterStatus.Retried);
    }

    [Test]
    public async Task RequeueDeadLettersAsync_ManifestAlreadyQueued_IsSkipped()
    {
        await using var fx = await CreateWithTwoManifestsAsync("dl-q2a", "dl-q2b");
        await SeedQueuedEntryAsync(fx, "dl-q2a");
        var blocked = await SeedDeadLetterAsync(fx, "dl-q2a");
        var free = await SeedDeadLetterAsync(fx, "dl-q2b");

        var result = await fx.Scheduler.RequeueDeadLettersAsync(new[] { blocked.Id, free.Id });

        result.Count.Should().Be(1);
        result.Message.Should().Contain("1 skipped");
        (await QueuedFor(fx, "dl-q2a")).Should().Be(1);
    }

    [Test]
    public async Task RequeueAllDeadLettersAsync_TwoDeadLettersForOneManifest_CollapseToOneQueuedEntry()
    {
        await using var fx = await CreateWithManifestAsync("dl-q3");
        var first = await SeedDeadLetterAsync(fx, "dl-q3");
        var second = await SeedDeadLetterAsync(fx, "dl-q3");

        var result = await fx.Scheduler.RequeueAllDeadLettersAsync();

        result
            .Count.Should()
            .Be(2, "both are resolved, since a requeue re-runs the manifest's own input");
        result.Message.Should().Contain("1 folded");
        (await QueuedFor(fx, "dl-q3")).Should().Be(1);
        var resolved = await fx
            .DataContext.DeadLetters.AsNoTracking()
            .Where(d => d.Id == first.Id || d.Id == second.Id)
            .ToListAsync();
        resolved.Should().AllSatisfy(d => d.Status.Should().Be(DeadLetterStatus.Retried));
        var entry = await fx.DataContext.WorkQueues.AsNoTracking().SingleAsync();
        resolved
            .Should()
            .AllSatisfy(d => d.ResolutionNote.Should().Contain($"WorkQueue {entry.Id}"));
    }

    [Test]
    public async Task RequeueDeadLetterAsync_ManifestAlreadyQueued_IsRefusedAndLeavesTheDeadLetter()
    {
        await using var fx = await CreateWithManifestAsync("dl-q4");
        await SeedQueuedEntryAsync(fx, "dl-q4");
        var dl = await SeedDeadLetterAsync(fx, "dl-q4");

        var result = await fx.Scheduler.RequeueDeadLetterAsync(dl.Id);

        result.Success.Should().BeFalse();
        result.Message.Should().Contain("already has a queued entry");
        (await QueuedFor(fx, "dl-q4")).Should().Be(1);
        (await fx.DataContext.DeadLetters.AsNoTracking().SingleAsync(d => d.Id == dl.Id))
            .Status.Should()
            .Be(DeadLetterStatus.AwaitingIntervention);
    }

    [Test]
    public async Task Two_concurrent_requeue_alls_for_one_manifest_both_succeed_and_queue_one_entry()
    {
        await using var fx = await CreateWithManifestAsync("dl-race");
        var dl = await SeedDeadLetterAsync(fx, "dl-race");

        // Hold both calls between their "already queued?" check and their insert, so both pass
        // the check before either writes: the window a concurrent requeue or the ManifestManager
        // can land in.
        var scheduler = (Trax.Scheduler.Services.TraxScheduler.TraxScheduler)fx.Scheduler;
        using var bothChecked = new Barrier(2);
        scheduler.BeforeRequeueInsert = _ =>
            Task.Run(() => bothChecked.SignalAndWait(TimeSpan.FromSeconds(10)));

        var results = await Task.WhenAll(
            Task.Run(() => fx.Scheduler.RequeueAllDeadLettersAsync()),
            Task.Run(() => fx.Scheduler.RequeueAllDeadLettersAsync())
        );

        results.Select(r => r.Count).Sum().Should().Be(1, "the dead letter is resolved once");
        (await QueuedFor(fx, "dl-race")).Should().Be(1);
        (await fx.DataContext.DeadLetters.AsNoTracking().SingleAsync(d => d.Id == dl.Id))
            .Status.Should()
            .Be(DeadLetterStatus.Retried);
    }

    [Test]
    public async Task RequeueDeadLettersAsync_MoreIdsThanOneBatchTakes_IsRefusedAndRequeuesNothing()
    {
        await using var fx = await CreateWithManifestAsync("dl-cap-r");
        var dl = await SeedDeadLetterAsync(fx, "dl-cap-r");
        var ids = Enumerable
            .Range(1, OperationsService.MaxBatchSize)
            .Select(i => 900_000_000L + i)
            .Append(dl.Id)
            .ToArray();

        var result = await fx.Scheduler.RequeueDeadLettersAsync(ids);

        result.Count.Should().Be(0);
        result.Message.Should().Contain($"At most {OperationsService.MaxBatchSize} ids");
        (await fx.DataContext.DeadLetters.AsNoTracking().SingleAsync(d => d.Id == dl.Id))
            .Status.Should()
            .Be(DeadLetterStatus.AwaitingIntervention);
        (await QueuedFor(fx, "dl-cap-r")).Should().Be(0);
    }

    [Test]
    public async Task AcknowledgeDeadLettersAsync_MoreIdsThanOneBatchTakes_IsRefusedAndAcknowledgesNothing()
    {
        await using var fx = await CreateWithManifestAsync("dl-cap-a");
        var dl = await SeedDeadLetterAsync(fx, "dl-cap-a");
        var ids = Enumerable
            .Range(1, OperationsService.MaxBatchSize)
            .Select(i => 900_000_000L + i)
            .Append(dl.Id)
            .ToArray();

        var result = await fx.Scheduler.AcknowledgeDeadLettersAsync(ids, "too many");

        result.Count.Should().Be(0);
        result.Message.Should().Contain($"At most {OperationsService.MaxBatchSize} ids");
        (await fx.DataContext.DeadLetters.AsNoTracking().SingleAsync(d => d.Id == dl.Id))
            .Status.Should()
            .Be(DeadLetterStatus.AwaitingIntervention);
    }

    [Test]
    public async Task RequeueAllDeadLettersAsync_AcrossSeveralPages_RequeuesEveryManifestAndFoldsWithinOne()
    {
        var externalIds = Enumerable.Range(1, 5).Select(i => $"dl-page-{i}").ToArray();
        await using var fx = await SchedulerE2EFixture.CreateAsync(s =>
        {
            foreach (var id in externalIds)
                s.Schedule<ISchedulerTestTrain>(id, new SchedulerTestInput(), Every.Minutes(5));
        });
        await fx.MaterializePendingManifestsAsync();
        foreach (var id in externalIds)
            await SeedDeadLetterAsync(fx, id);
        await SeedDeadLetterAsync(fx, "dl-page-3");
        await SeedQueuedEntryAsync(fx, "dl-page-5");

        // Two manifests a page, so five manifests take three pages.
        ((Trax.Scheduler.Services.TraxScheduler.TraxScheduler)fx.Scheduler).RequeueAllPageSize = 2;

        var result = await fx.Scheduler.RequeueAllDeadLettersAsync();

        result.Count.Should().Be(5, "four manifests' dead letters, two of them for one manifest");
        result.Message.Should().Contain("1 folded").And.Contain("1 skipped");
        foreach (var id in externalIds)
            (await QueuedFor(fx, id)).Should().Be(1, $"{id} has exactly one queued entry");
        (
            await fx
                .DataContext.DeadLetters.AsNoTracking()
                .CountAsync(d => d.Status == DeadLetterStatus.AwaitingIntervention)
        )
            .Should()
            .Be(1, "only the dead letter whose manifest was already queued is left");
    }

    #endregion

    #region Acknowledge in one statement

    [Test]
    public async Task AcknowledgeAllDeadLettersAsync_ResolvesEveryAwaitingOneAndLeavesResolvedOnesAlone()
    {
        await using var fx = await CreateWithManifestAsync("dl-a1");
        var awaiting = await SeedDeadLetterAsync(fx, "dl-a1");
        var done = await SeedDeadLetterAsync(fx, "dl-a1");
        await fx.Scheduler.AcknowledgeDeadLetterAsync(done.Id, "earlier");
        var before = DateTime.UtcNow;

        var result = await fx.Scheduler.AcknowledgeAllDeadLettersAsync("clearing");

        result.Count.Should().Be(1);
        fx.DataContext.Reset();
        var rows = await fx.DataContext.DeadLetters.AsNoTracking().ToDictionaryAsync(d => d.Id);
        rows[awaiting.Id].Status.Should().Be(DeadLetterStatus.Acknowledged);
        rows[awaiting.Id].ResolutionNote.Should().Be("clearing");
        rows[awaiting.Id].ResolvedAt.Should().BeOnOrAfter(before.AddSeconds(-1));
        rows[done.Id].ResolutionNote.Should().Be("earlier", "an already resolved one is untouched");
    }

    [Test]
    public async Task AcknowledgeDeadLettersAsync_OnlyTheListedAwaitingOnes()
    {
        await using var fx = await CreateWithManifestAsync("dl-a2");
        var listed = await SeedDeadLetterAsync(fx, "dl-a2");
        var unlisted = await SeedDeadLetterAsync(fx, "dl-a2");

        var result = await fx.Scheduler.AcknowledgeDeadLettersAsync(new[] { listed.Id }, "one");

        result.Count.Should().Be(1);
        fx.DataContext.Reset();
        var rows = await fx.DataContext.DeadLetters.AsNoTracking().ToDictionaryAsync(d => d.Id);
        rows[listed.Id].Status.Should().Be(DeadLetterStatus.Acknowledged);
        rows[unlisted.Id].Status.Should().Be(DeadLetterStatus.AwaitingIntervention);
    }

    #endregion
}
