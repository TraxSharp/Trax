using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
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
using Trax.Scheduler.Services.SchedulerStartupService;
using Trax.Scheduler.Services.TraxScheduler;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Integration tests for orphaned manifest pruning during scheduler startup.
/// Verifies that manifests no longer defined in the startup configuration are
/// automatically deleted along with their related data.
/// </summary>
[TestFixture]
public class OrphanManifestPruningTests : TestSetup
{
    #region Orphan Pruning Tests

    [Test]
    public async Task StartAsync_WithOrphanedManifests_PrunesOrphansOnly()
    {
        // Arrange: Create 3 manifests in DB, configure only 2 as expected
        var manifestA = await CreateAndSaveManifestWithExternalId("keep-a");
        var manifestB = await CreateAndSaveManifestWithExternalId("keep-b");
        var orphan = await CreateAndSaveManifestWithExternalId("orphan-c");

        var configuration = CreateConfiguration(
            expectedExternalIds: ["keep-a", "keep-b"],
            pruneOrphanedManifests: true
        );

        var startupService = CreateStartupService(configuration);

        // Act
        await startupService.StartAsync(CancellationToken.None);

        // Assert
        DataContext.Reset();
        var remaining = await DataContext.Manifests.Select(m => m.ExternalId).ToListAsync();

        remaining.Should().Contain("keep-a");
        remaining.Should().Contain("keep-b");
        remaining.Should().NotContain("orphan-c", "orphaned manifests should be pruned");
    }

    [Test]
    public async Task StartAsync_WithOrphanedManifest_CascadeDeletesRelatedData()
    {
        // Arrange: Create orphan manifest with WorkQueue, DeadLetter, and Metadata
        var orphan = await CreateAndSaveManifestWithExternalId("orphan-with-data");
        var workQueue = await CreateAndSaveWorkQueueEntry(orphan);
        var deadLetter = await CreateAndSaveDeadLetter(orphan);
        var metadata = await CreateAndSaveMetadata(orphan, TrainState.Completed);

        var configuration = CreateConfiguration(
            expectedExternalIds: ["some-other-manifest"],
            pruneOrphanedManifests: true
        );

        // Create the "some-other-manifest" so expectedIds isn't referencing something nonexistent
        await CreateAndSaveManifestWithExternalId("some-other-manifest");

        var startupService = CreateStartupService(configuration);

        // Act
        await startupService.StartAsync(CancellationToken.None);

        // Assert
        DataContext.Reset();

        var remainingManifests = await DataContext.Manifests.ToListAsync();
        remainingManifests
            .Should()
            .HaveCount(1)
            .And.Subject.First()
            .ExternalId.Should()
            .Be("some-other-manifest");

        var remainingWorkQueues = await DataContext
            .WorkQueues.Where(w => w.ManifestId == orphan.Id)
            .ToListAsync();
        remainingWorkQueues
            .Should()
            .BeEmpty("work queue entries for orphaned manifest should be deleted");

        var remainingDeadLetters = await DataContext
            .DeadLetters.Where(d => d.ManifestId == orphan.Id)
            .ToListAsync();
        remainingDeadLetters
            .Should()
            .BeEmpty("dead letters for orphaned manifest should be deleted");

        var remainingMetadata = await DataContext
            .Metadatas.Where(m => m.ManifestId == orphan.Id)
            .ToListAsync();
        remainingMetadata.Should().BeEmpty("metadata for orphaned manifest should be deleted");
    }

    [Test]
    public async Task StartAsync_WithConfiguredManifests_LeavesThemUntouched()
    {
        // Arrange: Create manifests that are all in the expected set
        var manifestA = await CreateAndSaveManifestWithExternalId("configured-a");
        var manifestB = await CreateAndSaveManifestWithExternalId("configured-b");
        var metadataA = await CreateAndSaveMetadata(manifestA, TrainState.Completed);
        var workQueueB = await CreateAndSaveWorkQueueEntry(manifestB);

        var configuration = CreateConfiguration(
            expectedExternalIds: ["configured-a", "configured-b"],
            pruneOrphanedManifests: true
        );

        var startupService = CreateStartupService(configuration);

        // Act
        await startupService.StartAsync(CancellationToken.None);

        // Assert
        DataContext.Reset();

        var remaining = await DataContext.Manifests.Select(m => m.ExternalId).ToListAsync();
        remaining.Should().Contain("configured-a");
        remaining.Should().Contain("configured-b");

        var metadataCount = await DataContext
            .Metadatas.Where(m => m.ManifestId == manifestA.Id)
            .CountAsync();
        metadataCount.Should().Be(1, "metadata for configured manifest should be preserved");

        var workQueueCount = await DataContext
            .WorkQueues.Where(w => w.ManifestId == manifestB.Id)
            .CountAsync();
        workQueueCount.Should().Be(1, "work queue for configured manifest should be preserved");
    }

    [Test]
    public async Task StartAsync_WithBothOrphansAsDependents_DeletesBoth()
    {
        // Arrange: B depends on A, both are orphaned
        var parentOrphan = await CreateAndSaveManifestWithExternalId("orphan-parent");
        var childOrphan = await CreateAndSaveDependentManifest(parentOrphan, "orphan-child");

        var keptManifest = await CreateAndSaveManifestWithExternalId("kept-manifest");

        var configuration = CreateConfiguration(
            expectedExternalIds: ["kept-manifest"],
            pruneOrphanedManifests: true
        );

        var startupService = CreateStartupService(configuration);

        // Act
        await startupService.StartAsync(CancellationToken.None);

        // Assert
        DataContext.Reset();
        var remaining = await DataContext.Manifests.Select(m => m.ExternalId).ToListAsync();

        remaining.Should().HaveCount(1);
        remaining.Should().Contain("kept-manifest");
        remaining.Should().NotContain("orphan-parent");
        remaining.Should().NotContain("orphan-child");
    }

    [Test]
    public async Task StartAsync_WithKeptManifestDependingOnOrphan_NullsDependencyAndDeletesOrphan()
    {
        // Arrange: B (kept) depends on A (orphaned) — A removed from config but B is kept
        var orphanParent = await CreateAndSaveManifestWithExternalId("orphan-parent");
        var keptChild = await CreateAndSaveDependentManifest(orphanParent, "kept-child");

        var configuration = CreateConfiguration(
            expectedExternalIds: ["kept-child"],
            pruneOrphanedManifests: true
        );

        var startupService = CreateStartupService(configuration);

        // Act
        await startupService.StartAsync(CancellationToken.None);

        // Assert
        DataContext.Reset();
        var remaining = await DataContext.Manifests.ToListAsync();

        remaining.Should().HaveCount(1);
        remaining[0].ExternalId.Should().Be("kept-child");
        remaining[0]
            .DependsOnManifestId.Should()
            .BeNull("the FK referencing the orphaned parent should be cleared");
    }

    [Test]
    public async Task StartAsync_WithPruneOrphanedManifestsDisabled_LeavesOrphans()
    {
        // Arrange
        var orphan = await CreateAndSaveManifestWithExternalId("should-stay");
        var kept = await CreateAndSaveManifestWithExternalId("configured");

        var configuration = CreateConfiguration(
            expectedExternalIds: ["configured"],
            pruneOrphanedManifests: false
        );

        var startupService = CreateStartupService(configuration);

        // Act
        await startupService.StartAsync(CancellationToken.None);

        // Assert
        DataContext.Reset();
        var remaining = await DataContext.Manifests.Select(m => m.ExternalId).ToListAsync();

        remaining.Should().Contain("should-stay", "pruning is disabled, orphans should remain");
        remaining.Should().Contain("configured");
    }

    [Test]
    public async Task StartAsync_WithNoPendingManifestsAndPruningEnabled_DeletesAllManifests()
    {
        // Arrange: Manifests exist in DB but no PendingManifests configured (all schedules removed)
        await CreateAndSaveManifestWithExternalId("leftover-manifest");

        var configuration = new SchedulerConfiguration
        {
            PruneOrphanedManifests = true,
            RecoverStuckJobsOnStartup = false,
        };
        // PendingManifests is empty → expectedExternalIds is empty → all manifests are orphans

        var startupService = CreateStartupService(configuration);

        // Act
        await startupService.StartAsync(CancellationToken.None);

        // Assert
        DataContext.Reset();
        var remaining = await DataContext.Manifests.ToListAsync();

        remaining
            .Should()
            .BeEmpty("all manifests should be pruned when no schedules are configured");
    }

    [Test]
    public async Task StartAsync_WithNoPendingManifestsAndPruningDisabled_LeavesAllManifests()
    {
        // Arrange: Manifests exist in DB, no PendingManifests, but pruning is disabled
        await CreateAndSaveManifestWithExternalId("safe-manifest");

        var configuration = new SchedulerConfiguration
        {
            PruneOrphanedManifests = false,
            RecoverStuckJobsOnStartup = false,
        };

        var startupService = CreateStartupService(configuration);

        // Act
        await startupService.StartAsync(CancellationToken.None);

        // Assert
        DataContext.Reset();
        var remaining = await DataContext.Manifests.Select(m => m.ExternalId).ToListAsync();

        remaining
            .Should()
            .Contain("safe-manifest", "pruning is disabled so nothing should be deleted");
    }

    [Test]
    public async Task StartAsync_AfterPruning_CleansUpOrphanedManifestGroups()
    {
        // Arrange: Create orphan manifest in its own group, and a kept manifest in another group
        var orphanGroup = await TestSetup.CreateAndSaveManifestGroup(
            DataContext,
            name: "orphan-group"
        );
        var orphan = await CreateAndSaveManifestWithExternalId(
            "orphan-in-group",
            groupId: orphanGroup.Id
        );

        var keptGroup = await TestSetup.CreateAndSaveManifestGroup(DataContext, name: "kept-group");
        var kept = await CreateAndSaveManifestWithExternalId(
            "kept-in-group",
            groupId: keptGroup.Id
        );

        var configuration = CreateConfiguration(
            expectedExternalIds: ["kept-in-group"],
            pruneOrphanedManifests: true
        );

        var startupService = CreateStartupService(configuration);

        // Act
        await startupService.StartAsync(CancellationToken.None);

        // Assert
        DataContext.Reset();

        var remainingGroups = await DataContext.ManifestGroups.Select(g => g.Name).ToListAsync();

        remainingGroups.Should().Contain("kept-group");
        remainingGroups
            .Should()
            .NotContain(
                "orphan-group",
                "manifest group with no remaining manifests should be cleaned up"
            );
    }

    #endregion

    #region Helper Methods

    private SchedulerStartupService CreateStartupService(SchedulerConfiguration configuration)
    {
        var loggerFactory = Scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
        var startupLogger = loggerFactory.CreateLogger<SchedulerStartupService>();

        return new SchedulerStartupService(Scope.ServiceProvider, configuration, startupLogger);
    }

    private static SchedulerConfiguration CreateConfiguration(
        IEnumerable<string> expectedExternalIds,
        bool pruneOrphanedManifests = true
    )
    {
        var configuration = new SchedulerConfiguration
        {
            PruneOrphanedManifests = pruneOrphanedManifests,
            RecoverStuckJobsOnStartup = false,
        };

        // Add a no-op PendingManifest that carries the ExpectedExternalIds
        // (the ScheduleFunc is never called since we pre-populate the DB directly)
        var externalIds = expectedExternalIds.ToList();

        if (externalIds.Count > 0)
        {
            configuration.PendingManifests.Add(
                new PendingManifest
                {
                    ExternalId = "test-pending-manifest",
                    ExpectedExternalIds = externalIds,
                    ScheduleFunc = (_, _) => Task.FromResult<Manifest>(null!),
                }
            );
        }

        return configuration;
    }

    private async Task<Manifest> CreateAndSaveManifestWithExternalId(
        string externalId,
        long? groupId = null
    )
    {
        if (groupId is null)
        {
            var group = await TestSetup.CreateAndSaveManifestGroup(
                DataContext,
                name: $"group-{Guid.NewGuid():N}"
            );
            groupId = group.Id;
        }

        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(SchedulerTestTrain),
                IsEnabled = true,
                ScheduleType = ScheduleType.Interval,
                IntervalSeconds = 60,
                MaxRetries = 3,
                Properties = new SchedulerTestInput { Value = externalId },
            }
        );

        manifest.ExternalId = externalId;
        manifest.ManifestGroupId = groupId.Value;

        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        return manifest;
    }

    private async Task<Manifest> CreateAndSaveDependentManifest(Manifest parent, string externalId)
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

    private async Task<Metadata> CreateAndSaveMetadata(Manifest manifest, TrainState state)
    {
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(SchedulerTestTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = manifest.GetProperties<SchedulerTestInput>(),
                ManifestId = manifest.Id,
            }
        );

        metadata.TrainState = state;

        await DataContext.Track(metadata);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        return metadata;
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

    private async Task<WorkQueue> CreateAndSaveWorkQueueEntry(Manifest manifest)
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

    #endregion
}
