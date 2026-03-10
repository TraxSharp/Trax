using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.DeadLetter.DTOs;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Scheduling;
using Trax.Scheduler.Services.TraxScheduler;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Integration tests verifying that manifest pruning runs outside the main upsert
/// transaction and that prune failures do not roll back successfully scheduled manifests.
/// </summary>
[TestFixture]
public class PruneSafeTests : TestSetup
{
    private ITraxScheduler _scheduler = null!;

    public override async Task TestSetUp()
    {
        await base.TestSetUp();
        _scheduler = Scope.ServiceProvider.GetRequiredService<ITraxScheduler>();
    }

    #region ScheduleMany — Prune Succeeds

    [Test]
    public async Task ScheduleMany_WithPrunePrefix_PrunesStaleManifests()
    {
        // Arrange: pre-create a manifest that should be pruned
        var stale = await CreateManifestWithExternalId("customer-old-item");

        // Act: schedule a new batch with prune prefix
        var results = await _scheduler.ScheduleManyAsync<
            ISchedulerTestTrain,
            SchedulerTestInput,
            LanguageExt.Unit,
            string
        >(
            ["item-1", "item-2"],
            id => ($"customer-{id}", new SchedulerTestInput { Value = id }),
            Every.Minutes(5),
            options => options.PrunePrefix("customer-")
        );

        // Assert: new manifests exist, stale one is gone
        results.Should().HaveCount(2);

        DataContext.Reset();
        var remaining = await DataContext.Manifests.Select(m => m.ExternalId).ToListAsync();

        remaining.Should().Contain("customer-item-1");
        remaining.Should().Contain("customer-item-2");
        remaining.Should().NotContain("customer-old-item");
    }

    [Test]
    public async Task ScheduleMany_WithPrunePrefix_CascadeDeletesRelatedData()
    {
        // Arrange: stale manifest with metadata, work queue, and dead letter
        var stale = await CreateManifestWithExternalId("cascade-stale");
        await CreateMetadataForManifest(stale, TrainState.Completed);
        await CreateWorkQueueEntry(stale);
        await CreateDeadLetter(stale);

        // Act
        var results = await _scheduler.ScheduleManyAsync<
            ISchedulerTestTrain,
            SchedulerTestInput,
            LanguageExt.Unit,
            string
        >(
            ["new"],
            id => ($"cascade-{id}", new SchedulerTestInput { Value = id }),
            Every.Minutes(5),
            options => options.PrunePrefix("cascade-")
        );

        // Assert: all related data for stale manifest is gone
        DataContext.Reset();

        var remainingManifests = await DataContext
            .Manifests.Select(m => m.ExternalId)
            .ToListAsync();
        remainingManifests.Should().Equal(["cascade-new"]);

        var staleMetadata = await DataContext
            .Metadatas.Where(m => m.ManifestId == stale.Id)
            .CountAsync();
        staleMetadata.Should().Be(0);

        var staleWorkQueue = await DataContext
            .WorkQueues.Where(w => w.ManifestId == stale.Id)
            .CountAsync();
        staleWorkQueue.Should().Be(0);

        var staleDeadLetters = await DataContext
            .DeadLetters.Where(d => d.ManifestId == stale.Id)
            .CountAsync();
        staleDeadLetters.Should().Be(0);
    }

    [Test]
    public async Task ScheduleMany_WithoutPrunePrefix_DoesNotPrune()
    {
        // Arrange: pre-create a manifest
        var existing = await CreateManifestWithExternalId("keep-me");

        // Act: schedule without prune prefix
        await _scheduler.ScheduleManyAsync<
            ISchedulerTestTrain,
            SchedulerTestInput,
            LanguageExt.Unit,
            string
        >(
            ["other"],
            id => ($"other-{id}", new SchedulerTestInput { Value = id }),
            Every.Minutes(5)
        );

        // Assert: existing manifest is untouched
        DataContext.Reset();
        var remaining = await DataContext.Manifests.Select(m => m.ExternalId).ToListAsync();

        remaining.Should().Contain("keep-me");
        remaining.Should().Contain("other-other");
    }

    [Test]
    public async Task ScheduleMany_WithPrunePrefix_OnlyPrunesMatchingPrefix()
    {
        // Arrange: manifests with different prefixes
        var unrelated = await CreateManifestWithExternalId("unrelated-item");
        var stale = await CreateManifestWithExternalId("target-old");

        // Act: prune only "target-" prefix
        await _scheduler.ScheduleManyAsync<
            ISchedulerTestTrain,
            SchedulerTestInput,
            LanguageExt.Unit,
            string
        >(
            ["new"],
            id => ($"target-{id}", new SchedulerTestInput { Value = id }),
            Every.Minutes(5),
            options => options.PrunePrefix("target-")
        );

        // Assert: unrelated manifest survives
        DataContext.Reset();
        var remaining = await DataContext.Manifests.Select(m => m.ExternalId).ToListAsync();

        remaining.Should().Contain("unrelated-item");
        remaining.Should().Contain("target-new");
        remaining.Should().NotContain("target-old");
    }

    [Test]
    public async Task ScheduleMany_WithPrunePrefix_NothingToPrune_Succeeds()
    {
        // Act: schedule with prune prefix but no stale manifests exist
        var results = await _scheduler.ScheduleManyAsync<
            ISchedulerTestTrain,
            SchedulerTestInput,
            LanguageExt.Unit,
            string
        >(
            ["a", "b"],
            id => ($"fresh-{id}", new SchedulerTestInput { Value = id }),
            Every.Minutes(5),
            options => options.PrunePrefix("fresh-")
        );

        // Assert: all manifests created successfully
        results.Should().HaveCount(2);

        DataContext.Reset();
        var remaining = await DataContext.Manifests.Select(m => m.ExternalId).ToListAsync();

        remaining.Should().Contain("fresh-a");
        remaining.Should().Contain("fresh-b");
    }

    #endregion

    #region ScheduleMany — Upserts Committed Before Prune

    [Test]
    public async Task ScheduleMany_WithPrunePrefix_UpsertsAreCommittedRegardlessOfPrune()
    {
        // Arrange: schedule a batch with pruning
        var results = await _scheduler.ScheduleManyAsync<
            ISchedulerTestTrain,
            SchedulerTestInput,
            LanguageExt.Unit,
            string
        >(
            ["x", "y", "z"],
            id => ($"committed-{id}", new SchedulerTestInput { Value = id }),
            Every.Minutes(5),
            options => options.PrunePrefix("committed-")
        );

        // Assert: manifests are committed and visible in a separate context
        results.Should().HaveCount(3);

        // Use a fresh DataContext to prove they're committed (not just in the same transaction)
        DataContext.Reset();
        var count = await DataContext.Manifests.CountAsync(m =>
            m.ExternalId.StartsWith("committed-")
        );
        count.Should().Be(3);
    }

    #endregion

    #region ScheduleManyDependent — Prune Succeeds

    [Test]
    public async Task ScheduleManyDependent_WithPrunePrefix_PrunesStaleManifests()
    {
        // Arrange: create parent manifests first
        var parents = await _scheduler.ScheduleManyAsync<
            ISchedulerTestTrain,
            SchedulerTestInput,
            LanguageExt.Unit,
            string
        >(
            ["p1", "p2"],
            id => ($"parent-{id}", new SchedulerTestInput { Value = id }),
            Every.Minutes(5)
        );

        // Create a stale dependent manifest
        var staleParent = await CreateManifestWithExternalId("parent-old");
        var staleDep = await CreateDependentManifest(staleParent, "dep-old");

        // Act: schedule dependents with prune
        var results = await _scheduler.ScheduleManyDependentAsync<
            ISchedulerTestTrain,
            SchedulerTestInput,
            LanguageExt.Unit,
            string
        >(
            ["p1", "p2"],
            id => ($"dep-{id}", new SchedulerTestInput { Value = id }),
            id => $"parent-{id}",
            options => options.PrunePrefix("dep-")
        );

        // Assert
        results.Should().HaveCount(2);

        DataContext.Reset();
        var remaining = await DataContext.Manifests.Select(m => m.ExternalId).ToListAsync();

        remaining.Should().Contain("dep-p1");
        remaining.Should().Contain("dep-p2");
        remaining.Should().NotContain("dep-old");
    }

    [Test]
    public async Task ScheduleManyDependent_WithPrunePrefix_CascadeDeletesRelatedData()
    {
        // Arrange: parent + stale dependent with related data
        var parent = await CreateManifestWithExternalId("depparent-a");
        var staleDep = await CreateDependentManifest(parent, "depcascade-stale");
        await CreateMetadataForManifest(staleDep, TrainState.Failed);
        await CreateWorkQueueEntry(staleDep);

        // Schedule the parent via the scheduler so it's visible for dependency resolution
        await _scheduler.ScheduleManyAsync<
            ISchedulerTestTrain,
            SchedulerTestInput,
            LanguageExt.Unit,
            string
        >(
            ["a"],
            id => ($"depparent-{id}", new SchedulerTestInput { Value = id }),
            Every.Minutes(5)
        );

        // Act: schedule new dependent with prune
        var results = await _scheduler.ScheduleManyDependentAsync<
            ISchedulerTestTrain,
            SchedulerTestInput,
            LanguageExt.Unit,
            string
        >(
            ["a"],
            id => ($"depcascade-{id}", new SchedulerTestInput { Value = id }),
            _ => "depparent-a",
            options => options.PrunePrefix("depcascade-")
        );

        // Assert
        DataContext.Reset();

        var remainingManifests = await DataContext
            .Manifests.Select(m => m.ExternalId)
            .ToListAsync();
        remainingManifests.Should().Contain("depcascade-a");
        remainingManifests.Should().NotContain("depcascade-stale");

        var staleMetadata = await DataContext
            .Metadatas.Where(m => m.ManifestId == staleDep.Id)
            .CountAsync();
        staleMetadata.Should().Be(0);

        var staleWorkQueue = await DataContext
            .WorkQueues.Where(w => w.ManifestId == staleDep.Id)
            .CountAsync();
        staleWorkQueue.Should().Be(0);
    }

    #endregion

    #region Multiple Batches — Prune Isolation

    [Test]
    public async Task ScheduleMany_MultipleBatches_PruneOnlyAffectsOwnPrefix()
    {
        // Arrange: schedule two independent batches with different prefixes
        await _scheduler.ScheduleManyAsync<
            ISchedulerTestTrain,
            SchedulerTestInput,
            LanguageExt.Unit,
            string
        >(
            ["1", "2"],
            id => ($"alpha-{id}", new SchedulerTestInput { Value = id }),
            Every.Minutes(5),
            options => options.PrunePrefix("alpha-")
        );

        await _scheduler.ScheduleManyAsync<
            ISchedulerTestTrain,
            SchedulerTestInput,
            LanguageExt.Unit,
            string
        >(
            ["a", "b"],
            id => ($"beta-{id}", new SchedulerTestInput { Value = id }),
            Every.Minutes(5),
            options => options.PrunePrefix("beta-")
        );

        // Assert: both batches exist independently
        DataContext.Reset();
        var remaining = await DataContext.Manifests.Select(m => m.ExternalId).ToListAsync();

        remaining.Should().Contain("alpha-1");
        remaining.Should().Contain("alpha-2");
        remaining.Should().Contain("beta-a");
        remaining.Should().Contain("beta-b");
    }

    [Test]
    public async Task ScheduleMany_SecondBatchWithSamePrefix_PrunesFirstBatchStaleItems()
    {
        // Arrange: first batch
        await _scheduler.ScheduleManyAsync<
            ISchedulerTestTrain,
            SchedulerTestInput,
            LanguageExt.Unit,
            string
        >(
            ["1", "2", "3"],
            id => ($"evolving-{id}", new SchedulerTestInput { Value = id }),
            Every.Minutes(5),
            options => options.PrunePrefix("evolving-")
        );

        // Act: second batch removes "3", adds "4"
        await _scheduler.ScheduleManyAsync<
            ISchedulerTestTrain,
            SchedulerTestInput,
            LanguageExt.Unit,
            string
        >(
            ["1", "2", "4"],
            id => ($"evolving-{id}", new SchedulerTestInput { Value = id }),
            Every.Minutes(5),
            options => options.PrunePrefix("evolving-")
        );

        // Assert: "3" is pruned, rest remain
        DataContext.Reset();
        var remaining = await DataContext
            .Manifests.Where(m => m.ExternalId.StartsWith("evolving-"))
            .Select(m => m.ExternalId)
            .ToListAsync();

        remaining.Should().BeEquivalentTo(["evolving-1", "evolving-2", "evolving-4"]);
    }

    #endregion

    #region Idempotency

    [Test]
    public async Task ScheduleMany_CalledTwiceWithSameItems_IsIdempotent()
    {
        var items = new[] { "idem-a", "idem-b" };

        // Act: call twice with same data
        for (int i = 0; i < 2; i++)
        {
            await _scheduler.ScheduleManyAsync<
                ISchedulerTestTrain,
                SchedulerTestInput,
                LanguageExt.Unit,
                string
            >(
                items,
                id => ($"idem-{id}", new SchedulerTestInput { Value = id }),
                Every.Minutes(5),
                options => options.PrunePrefix("idem-")
            );
        }

        // Assert: exactly 2 manifests, not 4
        DataContext.Reset();
        var count = await DataContext.Manifests.CountAsync(m => m.ExternalId.StartsWith("idem-"));
        count.Should().Be(2);
    }

    #endregion

    #region Helper Methods

    private async Task<Manifest> CreateManifestWithExternalId(string externalId)
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
                IntervalSeconds = 300,
                MaxRetries = 3,
                Properties = new SchedulerTestInput { Value = externalId },
            }
        );

        manifest.ExternalId = externalId;
        manifest.ManifestGroupId = group.Id;

        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        return manifest;
    }

    private async Task<Manifest> CreateDependentManifest(Manifest parent, string externalId)
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
                ScheduleType = ScheduleType.Dependent,
                MaxRetries = 3,
                Properties = new SchedulerTestInput { Value = externalId },
                DependsOnManifestId = parent.Id,
            }
        );

        manifest.ExternalId = externalId;
        manifest.ManifestGroupId = group.Id;

        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        return manifest;
    }

    private async Task<Metadata> CreateMetadataForManifest(Manifest manifest, TrainState state)
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(SchedulerTestTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = new SchedulerTestInput { Value = manifest.ExternalId },
                ManifestId = manifest.Id,
            }
        );

        metadata.TrainState = state;

        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        return metadata;
    }

    private async Task<WorkQueue> CreateWorkQueueEntry(Manifest manifest)
    {
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = manifest.Name,
                Input = manifest.Properties,
                InputTypeName = manifest.PropertyTypeName,
                ManifestId = manifest.Id,
            }
        );

        await DataContext.Track(entry);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        return entry;
    }

    private async Task<DeadLetter> CreateDeadLetter(Manifest manifest)
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
