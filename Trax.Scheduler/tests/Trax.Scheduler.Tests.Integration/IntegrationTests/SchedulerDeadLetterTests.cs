using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using Trax.Effect.Enums;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.DeadLetter.DTOs;
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
}
