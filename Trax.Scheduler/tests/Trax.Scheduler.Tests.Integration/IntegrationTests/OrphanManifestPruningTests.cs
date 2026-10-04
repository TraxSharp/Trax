using System.Data.Common;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using Trax.Effect.Data.Postgres.Services.PostgresContext;
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
    private const string Owner = "orphan-pruning-tests";

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
    public async Task StartAsync_WithNoPendingManifestsAndPruningEnabled_LeavesExistingManifests()
    {
        // Arrange: Manifests exist in DB but this host declares no schedules (an API or worker
        // host that calls AddScheduler only to reach the scheduler services).
        var leftover = await CreateAndSaveManifestWithExternalId("leftover-manifest");
        await CreateAndSaveMetadata(leftover, TrainState.Completed);

        var configuration = new SchedulerConfiguration
        {
            PruneOrphanedManifests = true,
            RecoverStuckJobsOnStartup = false,
            HasDatabaseProvider = true,
        };

        var startupService = CreateStartupService(configuration);

        // Act
        await startupService.StartAsync(CancellationToken.None);

        // Assert
        DataContext.Reset();
        var remaining = await DataContext.Manifests.Select(m => m.ExternalId).ToListAsync();
        remaining
            .Should()
            .Contain(
                "leftover-manifest",
                "a host that declares no schedules has no basis to call any manifest orphaned"
            );
        (await DataContext.Metadatas.AnyAsync(m => m.ManifestId == leftover.Id)).Should().BeTrue();
    }

    [TestCase(TrainState.InProgress)]
    [TestCase(TrainState.Pending)]
    public async Task StartAsync_WithAnOrphanWhoseRunIsActive_KeepsTheOrphanAndItsRun(
        TrainState state
    )
    {
        // Arrange: the orphan has a run that has not finished
        await CreateAndSaveManifestWithExternalId("keep-me");
        var orphan = await CreateAndSaveManifestWithExternalId("orphan-running");
        var run = await CreateAndSaveMetadata(orphan, state);
        var finishedOrphan = await CreateAndSaveManifestWithExternalId("orphan-finished");
        await CreateAndSaveMetadata(finishedOrphan, TrainState.Completed);

        var configuration = CreateConfiguration(expectedExternalIds: ["keep-me"]);
        var startupService = CreateStartupService(configuration);

        // Act
        await startupService.StartAsync(CancellationToken.None);

        // Assert
        DataContext.Reset();
        (await DataContext.Metadatas.AnyAsync(m => m.Id == run.Id))
            .Should()
            .BeTrue("a prune never deletes a run that has not finished");
        (await DataContext.Manifests.AnyAsync(m => m.Id == orphan.Id))
            .Should()
            .BeTrue("the manifest is kept until its run finishes, and pruned at a later start");
        (await DataContext.Manifests.AnyAsync(m => m.Id == finishedOrphan.Id))
            .Should()
            .BeFalse("an orphan with only finished runs is still pruned");
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
            HasDatabaseProvider = true,
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

    [Test]
    public async Task StartAsync_WithMoreOrphansThanBatchSize_PrunesAllOrphans()
    {
        // Arrange: Create more orphaned manifests than PruneBatchSize to verify
        // the batching loop processes all batches until complete.
        var orphanCount = SchedulerStartupService.PruneBatchSize + 10;
        var kept = await CreateAndSaveManifestWithExternalId("kept-manifest");

        for (var i = 0; i < orphanCount; i++)
            await CreateAndSaveManifestWithExternalId($"orphan-{i}");

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
    }

    [Test]
    public async Task StartAsync_WithLargeExpectedIdSet_PrunesOnlyOrphans()
    {
        // Arrange: Simulate a production scenario with many expected manifests and a few orphans.
        // The previous implementation generated NOT IN('id1', ..., 'id100') which could timeout
        // with large expected sets. The new implementation loads all IDs and filters in C#.
        var expectedCount = 100;
        var orphanCount = 5;

        var expectedIds = new List<string>();
        for (var i = 0; i < expectedCount; i++)
        {
            var externalId = $"expected-{i}";
            await CreateAndSaveManifestWithExternalId(externalId);
            expectedIds.Add(externalId);
        }

        for (var i = 0; i < orphanCount; i++)
            await CreateAndSaveManifestWithExternalId($"orphan-{i}");

        var configuration = CreateConfiguration(
            expectedExternalIds: expectedIds,
            pruneOrphanedManifests: true
        );

        var startupService = CreateStartupService(configuration);

        // Act
        await startupService.StartAsync(CancellationToken.None);

        // Assert
        DataContext.Reset();
        var remaining = await DataContext.Manifests.Select(m => m.ExternalId).ToListAsync();

        remaining.Should().HaveCount(expectedCount);
        remaining.Should().NotContain(m => m.StartsWith("orphan-"));
    }

    [Test]
    public async Task StartAsync_WithLargeExpectedIdSetAndOrphanData_CascadeDeletesRelatedData()
    {
        // Arrange: Large expected set + orphans with related data to verify cascade works
        // with the server-side filtering approach.
        var expectedIds = new List<string>();
        for (var i = 0; i < 50; i++)
        {
            var externalId = $"expected-{i}";
            await CreateAndSaveManifestWithExternalId(externalId);
            expectedIds.Add(externalId);
        }

        var orphan = await CreateAndSaveManifestWithExternalId("orphan-with-data");
        await CreateAndSaveWorkQueueEntry(orphan);
        await CreateAndSaveDeadLetter(orphan);
        await CreateAndSaveMetadata(orphan, TrainState.Completed);

        var configuration = CreateConfiguration(
            expectedExternalIds: expectedIds,
            pruneOrphanedManifests: true
        );

        var startupService = CreateStartupService(configuration);

        // Act
        await startupService.StartAsync(CancellationToken.None);

        // Assert
        DataContext.Reset();

        var remaining = await DataContext.Manifests.Select(m => m.ExternalId).ToListAsync();
        remaining.Should().HaveCount(50);
        remaining.Should().NotContain("orphan-with-data");

        var orphanWorkQueues = await DataContext
            .WorkQueues.Where(w => w.ManifestId == orphan.Id)
            .CountAsync();
        orphanWorkQueues.Should().Be(0);

        var orphanDeadLetters = await DataContext
            .DeadLetters.Where(d => d.ManifestId == orphan.Id)
            .CountAsync();
        orphanDeadLetters.Should().Be(0);

        var orphanMetadata = await DataContext
            .Metadatas.Where(m => m.ManifestId == orphan.Id)
            .CountAsync();
        orphanMetadata.Should().Be(0);
    }

    [Test]
    public async Task StartAsync_WhenEveryOrphanHasAnActiveRun_DeletesNothingAndReportsThemKept()
    {
        // Arrange: the only orphan's run is still pending, so its whole batch is kept
        await CreateAndSaveManifestWithExternalId("keep-me");
        var orphan = await CreateAndSaveManifestWithExternalId("orphan-pending");
        var run = await CreateAndSaveMetadata(orphan, TrainState.Pending);

        var logger = new CapturingLogger();
        var startupService = new SchedulerStartupService(
            Scope.ServiceProvider,
            CreateConfiguration(expectedExternalIds: ["keep-me"]),
            logger
        );

        // Act
        await startupService.StartAsync(CancellationToken.None);

        // Assert
        DataContext.Reset();
        (await DataContext.Manifests.AnyAsync(m => m.Id == orphan.Id)).Should().BeTrue();
        (await DataContext.Metadatas.AnyAsync(m => m.Id == run.Id)).Should().BeTrue();
        logger
            .Messages.Should()
            .Contain(
                "Finished pruning 0 orphaned manifest(s) from the database (1 kept until their runs finish)"
            );
    }

    [Test]
    public async Task StartAsync_WhenPruningFails_LogsTheFailureAndStillCleansUpEmptyGroups()
    {
        // Arrange: an orphan to prune, and a group with no manifests left
        await CreateAndSaveManifestWithExternalId("keep-me");
        var orphan = await CreateAndSaveManifestWithExternalId("orphan-unreachable");
        var emptyGroup = await TestSetup.CreateAndSaveManifestGroup(
            DataContext,
            name: $"empty-{Guid.NewGuid():N}"
        );

        // The prune's read of the orphans' runs fails, as a dropped connection would.
        var options = new DbContextOptionsBuilder<PostgresContext>(
            Scope.ServiceProvider.GetRequiredService<DbContextOptions<PostgresContext>>()
        )
            .AddInterceptors(new FailingMetadataReadInterceptor())
            .Options;
        await using var failingContext = new PostgresContext(options);
        var services = new ServiceCollection()
            .AddSingleton(Scope.ServiceProvider.GetRequiredService<ITraxScheduler>())
            .AddSingleton<IDataContext>(failingContext)
            .BuildServiceProvider();

        var logger = new CapturingLogger();
        var startupService = new SchedulerStartupService(
            services,
            CreateConfiguration(expectedExternalIds: ["keep-me"]),
            logger
        );

        // Act
        var act = () => startupService.StartAsync(CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync("pruning is housekeeping and does not stop the host");
        logger
            .Errors.Should()
            .ContainSingle()
            .Which.Should()
            .StartWith("Pruning orphaned manifests failed");
        DataContext.Reset();
        (await DataContext.Manifests.AnyAsync(m => m.Id == orphan.Id))
            .Should()
            .BeTrue("the failed prune deleted nothing, and the next start prunes again");
        (await DataContext.ManifestGroups.AnyAsync(g => g.Id == emptyGroup.Id))
            .Should()
            .BeFalse("the orphaned-group cleanup after the prune still runs");
    }

    private sealed class FailingMetadataReadInterceptor : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        ) =>
            command.CommandText.Contains("trax.metadata", StringComparison.Ordinal)
                ? throw new NpgsqlException("The connection was lost.")
                : ValueTask.FromResult(result);
    }

    private sealed class CapturingLogger : ILogger<SchedulerStartupService>
    {
        private readonly System.Collections.Concurrent.ConcurrentQueue<(
            LogLevel Level,
            string Message
        )> _entries = new();

        public IReadOnlyList<string> Messages => _entries.Select(e => e.Message).ToList();

        public IReadOnlyList<string> Errors =>
            _entries.Where(e => e.Level >= LogLevel.Error).Select(e => e.Message).ToList();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter
        ) => _entries.Enqueue((logLevel, formatter(state, exception)));
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
            HasDatabaseProvider = true,
            Owner = Owner,
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
        long? groupId = null,
        string? owner = Owner
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
        manifest.Owner = owner;

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
        manifest.Owner = Owner;

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
