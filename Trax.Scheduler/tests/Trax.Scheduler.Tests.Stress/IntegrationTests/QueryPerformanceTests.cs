using System.Diagnostics;
using System.Globalization;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.ManifestGroup;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.SchedulerStartupService;
using Trax.Scheduler.Services.Scheduling;
using Trax.Scheduler.Services.TraxScheduler;
using Trax.Scheduler.Tests.Stress.Fakes.Trains;
using Trax.Scheduler.Tests.Stress.Fixtures;

namespace Trax.Scheduler.Tests.Stress.IntegrationTests;

/// <summary>
/// Stress tests that seed large volumes of data and verify that critical database
/// queries complete within acceptable time. These tests catch missing indexes,
/// sequential scans, and lock contention issues before they hit production.
///
/// Run with: dotnet test --filter "TestCategory=Stress"
/// NOT run on every PR — intended for pre-release validation.
/// </summary>
[TestFixture]
[Category("Stress")]
[Explicit(
    "Stress suite: seeds and loads heavily. Run with dotnet test --filter TestCategory=Stress"
)]
public class QueryPerformanceTests : TestSetup
{
    /// <summary>
    /// Number of manifests to seed. Each represents a scheduled job definition.
    /// Production systems can have 1K-10K manifests.
    /// </summary>
    private const int ManifestCount = 500;

    /// <summary>
    /// Number of metadata rows per manifest. Each represents a historical execution.
    /// Production systems accumulate 100-1000+ per manifest over time.
    /// </summary>
    private const int MetadataPerManifest = 100;

    private IList<Manifest>? _manifests;
    private ManifestGroup? _group;

    public override async Task TestSetUp()
    {
        await base.TestSetUp().ConfigureAwait(false);

        // Seed baseline data: 500 manifests × 100 metadata = 50K metadata rows
        _group = await SeedManifestGroup("stress-baseline").ConfigureAwait(false);
        _manifests = await SeedManifests(ManifestCount, _group.NotNull().Id).ConfigureAwait(false);

        // Mix of states: 70% completed, 20% failed, 5% in-progress, 5% pending
        var completed = _manifests.NotNull().Take(350).ToList();
        var failed = _manifests.NotNull().Skip(350).Take(100).ToList();
        var inProgress = _manifests.NotNull().Skip(450).Take(25).ToList();
        var pending = _manifests.NotNull().Skip(475).Take(25).ToList();

        await SeedMetadata(completed, MetadataPerManifest, TrainState.Completed)
            .ConfigureAwait(false);
        await SeedMetadata(failed, MetadataPerManifest, TrainState.Failed).ConfigureAwait(false);
        await SeedMetadata(
                inProgress,
                MetadataPerManifest,
                TrainState.InProgress,
                DateTime.UtcNow.AddMinutes(-30)
            )
            .ConfigureAwait(false);
        await SeedMetadata(
                pending,
                MetadataPerManifest,
                TrainState.Pending,
                DateTime.UtcNow.AddMinutes(-10)
            )
            .ConfigureAwait(false);

        await TestContext
            .Out.WriteLineAsync(
                $"Seeded {ManifestCount} manifests, {ManifestCount * MetadataPerManifest} metadata rows"
            )
            .ConfigureAwait(false);
    }

    #region Stale Job Detection (ReapStalePendingMetadataJunction pattern)

    [Test]
    public async Task FindStalePendingMetadata_With50KRows_CompletesWithinTimeout()
    {
        // This query pattern: WHERE train_state='pending' AND start_time < cutoff
        // Without ix_metadata_train_state_start_time, this is a full sequential scan.
        var cutoff = DateTime.UtcNow - TimeSpan.FromMinutes(5);

        var elapsed = await AssertCompletesWithin(async () =>
        {
            var stale = await DataContext
                .Metadatas.Where(m => m.TrainState == TrainState.Pending && m.StartTime < cutoff)
                .Select(m => new
                {
                    m.Id,
                    m.Name,
                    m.StartTime,
                })
                .AsNoTracking()
                .ToListAsync()
                .ConfigureAwait(false);

            stale.Should().NotBeEmpty("test data includes pending metadata older than cutoff");
        });

        await TestContext
            .Out.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"FindStalePendingMetadata: {elapsed.TotalMilliseconds:F0}ms"
                )
            )
            .ConfigureAwait(false);
    }

    #endregion

    #region Stuck Job Recovery (SchedulerStartupService pattern)

    [Test]
    public async Task RecoverStuckJobs_With50KRows_CompletesWithinTimeout()
    {
        // This query pattern: WHERE train_state='in_progress' AND start_time < serverStartTime
        // Without composite index, scans all 50K rows.
        var serverStartTime = DateTime.UtcNow;

        var elapsed = await AssertCompletesWithin(async () =>
        {
            var stuck = await DataContext
                .Metadatas.Where(m =>
                    m.TrainState == TrainState.InProgress && m.StartTime < serverStartTime
                )
                .ToListAsync()
                .ConfigureAwait(false);

            stuck.Should().NotBeEmpty("test data includes in-progress metadata");
        });

        await TestContext
            .Out.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"RecoverStuckJobs: {elapsed.TotalMilliseconds:F0}ms"
                )
            )
            .ConfigureAwait(false);
    }

    #endregion

    #region Dispatch Capacity (LoadDispatchCapacityJunction pattern)

    [Test]
    public async Task LoadDispatchCapacity_With50KRows_CompletesWithinTimeout()
    {
        // This query: GroupJoin Metadatas→Manifests, filter active states, GroupBy ManifestGroupId
        // Without ix_metadata_manifest_id_train_state, the join + filter is expensive.
        var elapsed = await AssertCompletesWithin(async () =>
        {
            var activeCounts = await DataContext
                .Metadatas.Where(m =>
                    m.TrainState == TrainState.Pending || m.TrainState == TrainState.InProgress
                )
                .GroupJoin(
                    DataContext.Manifests,
                    m => m.ManifestId,
                    man => man.Id,
                    (m, manifests) => new { m, manifests }
                )
                .SelectMany(
                    x => x.manifests.DefaultIfEmpty(),
                    (x, man) =>
                        new { GroupId = man == null ? (long?)null : (long?)man.ManifestGroupId }
                )
                .GroupBy(x => x.GroupId)
                .Select(g => new { GroupId = g.Key, Count = g.Count() })
                .ToListAsync()
                .ConfigureAwait(false);

            activeCounts.Should().NotBeEmpty("test data includes active metadata");
        });

        await TestContext
            .Out.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"LoadDispatchCapacity: {elapsed.TotalMilliseconds:F0}ms"
                )
            )
            .ConfigureAwait(false);
    }

    #endregion

    #region Dispatch Capacity with Exclusion Filter (HashSet optimization)

    [Test]
    public async Task LoadDispatchCapacity_WithExclusionFilter_With50KRows_CompletesWithinTimeout()
    {
        // Reproduces the exact LoadDispatchCapacityJunction query pattern with excluded
        // train names. Before the HashSet fix, this generated 9+ NOT LIKE clauses which
        // prevented index usage. After the fix, it uses <> ALL(@excluded) with a single
        // parameterized array comparison.
        var excluded = new HashSet<string>(StringComparer.Ordinal)
        {
            "Trax.Scheduler.Trains.ManifestManager.IManifestManagerTrain",
            "Trax.Scheduler.Trains.ManifestManager.ManifestManagerTrain",
            "Trax.Scheduler.Trains.ManifestManager.InMemoryManifestManagerTrain",
            "Trax.Scheduler.Trains.JobRunner.IJobRunnerTrain",
            "Trax.Scheduler.Trains.JobRunner.JobRunnerTrain",
            "Trax.Scheduler.Trains.MetadataCleanup.IMetadataCleanupTrain",
            "Trax.Scheduler.Trains.MetadataCleanup.MetadataCleanupTrain",
            "Trax.Scheduler.Trains.JobDispatcher.IJobDispatcherTrain",
            "Trax.Scheduler.Trains.JobDispatcher.JobDispatcherTrain",
        };

        var elapsed = await AssertCompletesWithin(async () =>
        {
            var activeCounts = await DataContext
                .Metadatas.Where(m =>
                    !excluded.Contains(m.Name)
                    && (m.TrainState == TrainState.Pending || m.TrainState == TrainState.InProgress)
                )
                .GroupJoin(
                    DataContext.Manifests,
                    m => m.ManifestId,
                    man => man.Id,
                    (m, manifests) => new { m, manifests }
                )
                .SelectMany(
                    x => x.manifests.DefaultIfEmpty(),
                    (x, man) =>
                        new { GroupId = man == null ? (long?)null : (long?)man.ManifestGroupId }
                )
                .GroupBy(x => x.GroupId)
                .Select(g => new { GroupId = g.Key, Count = g.Count() })
                .ToListAsync()
                .ConfigureAwait(false);

            activeCounts.Should().NotBeEmpty("test data includes active metadata");
        });

        await TestContext
            .Out.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"LoadDispatchCapacity (with exclusion filter): {elapsed.TotalMilliseconds:F0}ms"
                )
            )
            .ConfigureAwait(false);
    }

    #endregion

    #region Batched Metadata Cleanup (DeleteExpiredMetadataJunction optimization)

    [Test]
    public async Task BatchedDeleteExpiredMetadata_With50KRows_CompletesWithinTimeout()
    {
        // Tests the batched deletion pattern from DeleteExpiredMetadataJunction.
        // Instead of a single massive DELETE, we load IDs in batches and delete
        // associated FK rows + metadata per batch. This limits row-level lock duration.
        var trainName = typeof(StressTestTrain).FullName.NotNull();
        var cutoff = DateTime.UtcNow.AddMinutes(-30);
        var batchSize = 1000;
        var whitelist = new HashSet<string>(StringComparer.Ordinal) { trainName };

        var elapsed = await AssertCompletesWithin(
            async () =>
            {
                var totalDeleted = 0;

                while (true)
                {
                    var batchIds = await DataContext
                        .Metadatas.Where(m => whitelist.Contains(m.Name))
                        .Where(m => m.StartTime < cutoff)
                        .Where(m =>
                            m.TrainState == TrainState.Completed
                            || m.TrainState == TrainState.Failed
                            || m.TrainState == TrainState.Cancelled
                        )
                        .Select(m => m.Id)
                        .Take(batchSize)
                        .ToListAsync()
                        .ConfigureAwait(false);

                    if (batchIds.Count == 0)
                        break;

                    await DataContext
                        .WorkQueues.Where(wq =>
                            wq.MetadataId.HasValue && batchIds.Contains(wq.MetadataId.Value)
                        )
                        .ExecuteDeleteAsync()
                        .ConfigureAwait(false);

                    await DataContext
                        .Logs.Where(l => batchIds.Contains(l.MetadataId))
                        .ExecuteDeleteAsync()
                        .ConfigureAwait(false);

                    totalDeleted += await DataContext
                        .Metadatas.Where(m => batchIds.Contains(m.Id))
                        .ExecuteDeleteAsync()
                        .ConfigureAwait(false);

                    if (batchIds.Count < batchSize)
                        break;
                }

                totalDeleted
                    .Should()
                    .BeGreaterThan(0, "expired metadata should exist in test data");
            },
            TimeSpan.FromSeconds(30)
        );

        await TestContext
            .Out.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"BatchedDeleteExpiredMetadata (batch={batchSize}): {elapsed.TotalMilliseconds:F0}ms"
                )
            )
            .ConfigureAwait(false);
    }

    [Test]
    public async Task BatchedDeleteExpiredMetadata_SmallBatches_CompletesWithinTimeout()
    {
        // Small batch size (100) exercises the loop more frequently.
        // Verifies overhead from multiple round-trips is acceptable.
        var trainName = typeof(StressTestTrain).FullName.NotNull();
        var cutoff = DateTime.UtcNow.AddMinutes(-30);
        var batchSize = 100;
        var whitelist = new HashSet<string>(StringComparer.Ordinal) { trainName };
        var batchCount = 0;

        var elapsed = await AssertCompletesWithin(
            async () =>
            {
                var totalDeleted = 0;

                while (true)
                {
                    var batchIds = await DataContext
                        .Metadatas.Where(m => whitelist.Contains(m.Name))
                        .Where(m => m.StartTime < cutoff)
                        .Where(m =>
                            m.TrainState == TrainState.Completed
                            || m.TrainState == TrainState.Failed
                            || m.TrainState == TrainState.Cancelled
                        )
                        .Select(m => m.Id)
                        .Take(batchSize)
                        .ToListAsync()
                        .ConfigureAwait(false);

                    if (batchIds.Count == 0)
                        break;

                    batchCount++;

                    await DataContext
                        .WorkQueues.Where(wq =>
                            wq.MetadataId.HasValue && batchIds.Contains(wq.MetadataId.Value)
                        )
                        .ExecuteDeleteAsync()
                        .ConfigureAwait(false);

                    await DataContext
                        .Logs.Where(l => batchIds.Contains(l.MetadataId))
                        .ExecuteDeleteAsync()
                        .ConfigureAwait(false);

                    totalDeleted += await DataContext
                        .Metadatas.Where(m => batchIds.Contains(m.Id))
                        .ExecuteDeleteAsync()
                        .ConfigureAwait(false);

                    if (batchIds.Count < batchSize)
                        break;
                }

                totalDeleted
                    .Should()
                    .BeGreaterThan(0, "expired metadata should exist in test data");
            },
            TimeSpan.FromSeconds(30)
        );

        await TestContext
            .Out.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"BatchedDeleteExpiredMetadata (batch={batchSize}, {batchCount} batches): {elapsed.TotalMilliseconds:F0}ms"
                )
            )
            .ConfigureAwait(false);
    }

    #endregion

    #region Cancel Timed-Out Jobs (CancelTimedOutJobsJunction pattern)

    [Test]
    public async Task FindTimedOutJobs_With50KRows_CompletesWithinTimeout()
    {
        // Query: WHERE manifest_id IN (...) AND train_state='in_progress' AND !cancellation_requested
        // Needs composite index on (manifest_id, train_state) for efficient filtering.
        var manifestIds = _manifests.NotNull().Select(m => m.Id).ToList();

        var elapsed = await AssertCompletesWithin(async () =>
        {
            var inProgress = await DataContext
                .Metadatas.Where(m =>
                    m.ManifestId != null
                    && manifestIds.Contains(m.ManifestId.Value)
                    && m.TrainState == TrainState.InProgress
                    && !m.CancellationRequested
                )
                .Select(m => new
                {
                    m.Id,
                    m.StartTime,
                    m.ManifestId,
                })
                .AsNoTracking()
                .ToListAsync()
                .ConfigureAwait(false);

            inProgress.Should().NotBeEmpty();
        });

        await TestContext
            .Out.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"FindTimedOutJobs: {elapsed.TotalMilliseconds:F0}ms"
                )
            )
            .ConfigureAwait(false);
    }

    #endregion

    #region Dashboard Time-Series Queries

    [Test]
    public async Task DashboardDailyCountByState_With50KRows_CompletesWithinTimeout()
    {
        // Dashboard KPI: GROUP BY train_state WHERE start_time in last 24h
        // Without ix_metadata_start_time_desc, this does a full sequential scan + sort.
        var cutoff = DateTime.UtcNow.AddDays(-1);

        var elapsed = await AssertCompletesWithin(async () =>
        {
            var counts = await DataContext
                .Metadatas.Where(m => m.StartTime >= cutoff)
                .GroupBy(m => m.TrainState)
                .Select(g => new { State = g.Key, Count = g.Count() })
                .ToListAsync()
                .ConfigureAwait(false);

            counts.Should().NotBeEmpty();
        });

        await TestContext
            .Out.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"DashboardDailyCount: {elapsed.TotalMilliseconds:F0}ms"
                )
            )
            .ConfigureAwait(false);
    }

    [Test]
    public async Task DashboardPaginatedExecutions_With50KRows_CompletesWithinTimeout()
    {
        // API GetExecutions: ORDER BY start_time DESC, SKIP/TAKE pagination
        // Without index on start_time, this sorts 50K rows every request.
        var elapsed = await AssertCompletesWithin(async () =>
        {
            var page = await DataContext
                .Metadatas.OrderByDescending(m => m.StartTime)
                .Skip(0)
                .Take(50)
                .AsNoTracking()
                .ToListAsync()
                .ConfigureAwait(false);

            page.Should().HaveCount(50);
        });

        await TestContext
            .Out.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"PaginatedExecutions: {elapsed.TotalMilliseconds:F0}ms"
                )
            )
            .ConfigureAwait(false);
    }

    #endregion

    #region LoadManifestsJunction Pattern (Subquery aggregation)

    [Test]
    public async Task LoadManifestsWithAggregates_With50KMetadata_CompletesWithinTimeout()
    {
        // LoadManifestsJunction: SELECT manifests + COUNT/EXISTS subqueries on child tables
        // With 500 manifests and 50K metadata, subqueries run 500 times.
        await SeedWorkQueues(_manifests.NotNull().Take(50).ToList());

        var elapsed = await AssertCompletesWithin(async () =>
        {
            var views = await DataContext
                .Manifests.Where(m => m.IsEnabled)
                .Select(m => new
                {
                    m.Id,
                    m.ExternalId,
                    FailedCount = m.Metadatas.Count(md => md.TrainState == TrainState.Failed),
                    HasQueuedWork = m.WorkQueues.Any(q => q.Status == WorkQueueStatus.Queued),
                    HasActiveExecution = m.Metadatas.Any(md =>
                        md.TrainState == TrainState.Pending
                        || md.TrainState == TrainState.InProgress
                    ),
                })
                .AsNoTracking()
                .ToListAsync()
                .ConfigureAwait(false);

            views.Should().HaveCount(ManifestCount);
        });

        await TestContext
            .Out.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"LoadManifestsWithAggregates: {elapsed.TotalMilliseconds:F0}ms"
                )
            )
            .ConfigureAwait(false);
    }

    #endregion

    #region Metadata Cleanup (DeleteExpiredMetadataJunction pattern)

    [Test]
    public async Task DeleteExpiredMetadata_With50KRows_CompletesWithinTimeout()
    {
        // DeleteExpiredMetadataJunction: DELETE WHERE name IN (whitelist) AND start_time < cutoff
        // AND train_state IN (completed, failed, cancelled). Uses subquery for cascade.
        var trainName = typeof(StressTestTrain).FullName.NotNull();
        var cutoff = DateTime.UtcNow.AddMinutes(-30);

        var elapsed = await AssertCompletesWithin(
            async () =>
            {
                var metadataIdsToDelete = DataContext
                    .Metadatas.Where(m => m.Name == trainName)
                    .Where(m => m.StartTime < cutoff)
                    .Where(m =>
                        m.TrainState == TrainState.Completed
                        || m.TrainState == TrainState.Failed
                        || m.TrainState == TrainState.Cancelled
                    )
                    .Select(m => m.Id);

                // Delete work queues referencing expired metadata
                await DataContext
                    .WorkQueues.Where(wq =>
                        wq.MetadataId.HasValue && metadataIdsToDelete.Contains(wq.MetadataId.Value)
                    )
                    .ExecuteDeleteAsync()
                    .ConfigureAwait(false);

                // Delete the metadata itself
                var deleted = await DataContext
                    .Metadatas.Where(m => m.Name == trainName)
                    .Where(m => m.StartTime < cutoff)
                    .Where(m =>
                        m.TrainState == TrainState.Completed
                        || m.TrainState == TrainState.Failed
                        || m.TrainState == TrainState.Cancelled
                    )
                    .ExecuteDeleteAsync()
                    .ConfigureAwait(false);

                deleted.Should().BeGreaterThan(0, "expired metadata should exist in test data");
            },
            TimeSpan.FromSeconds(30)
        );

        await TestContext
            .Out.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"DeleteExpiredMetadata: {elapsed.TotalMilliseconds:F0}ms"
                )
            )
            .ConfigureAwait(false);
    }

    #endregion

    #region Manifest Pruning (PruneStaleManifestsAsync pattern)

    [Test]
    public async Task PruneStaleManifests_WithLargeKeepSet_CompletesWithinTimeout()
    {
        // PruneStaleManifests: WHERE external_id LIKE 'prefix%' AND NOT IN (keepIds)
        // Then cascade DELETE on work_queue, dead_letter, metadata, manifest.
        // The original production issue: this inside a transaction caused timeouts.

        // Add some work queues and dead letters to make cascade realistic
        await SeedWorkQueues(_manifests.NotNull().Take(100).ToList());
        await SeedDeadLetters(_manifests.NotNull().Take(50).ToList(), 1);

        // Keep 400 manifests, prune 100
        var keepIds = _manifests
            .NotNull()
            .Take(400)
            .Select(m => m.ExternalId)
            .ToHashSet(StringComparer.Ordinal);

        var elapsed = await AssertCompletesWithin(
            async () =>
            {
                var staleManifestIds = await DataContext
                    .Manifests.Where(m =>
                        m.ExternalId.StartsWith("stress-") && !keepIds.Contains(m.ExternalId)
                    )
                    .Select(m => m.Id)
                    .ToListAsync()
                    .ConfigureAwait(false);

                staleManifestIds.Should().HaveCount(100);

                await DataContext
                    .WorkQueues.Where(w =>
                        w.ManifestId.HasValue && staleManifestIds.Contains(w.ManifestId.Value)
                    )
                    .ExecuteDeleteAsync()
                    .ConfigureAwait(false);

                await DataContext
                    .DeadLetters.Where(d => staleManifestIds.Contains(d.ManifestId))
                    .ExecuteDeleteAsync()
                    .ConfigureAwait(false);

                await DataContext
                    .Metadatas.Where(m =>
                        m.ManifestId.HasValue && staleManifestIds.Contains(m.ManifestId.Value)
                    )
                    .ExecuteDeleteAsync()
                    .ConfigureAwait(false);

                await DataContext
                    .Manifests.Where(m => staleManifestIds.Contains(m.Id))
                    .ExecuteDeleteAsync()
                    .ConfigureAwait(false);
            },
            TimeSpan.FromSeconds(30)
        );

        await TestContext
            .Out.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"PruneStaleManifests (100 pruned, 400 kept): {elapsed.TotalMilliseconds:F0}ms"
                )
            )
            .ConfigureAwait(false);
    }

    #endregion

    #region ScheduleMany at Scale

    [Test]
    public async Task ScheduleMany_5000Items_WithPrune_CompletesWithinTimeout()
    {
        // Real-world scenario: scheduling 5K manifests (e.g., one per customer/entity)
        // with PrunePrefix to remove stale ones. This is the exact production pattern
        // that caused the original timeout.
        var scheduler = Scope.ServiceProvider.GetRequiredService<ITraxScheduler>();

        var elapsed = await AssertCompletesWithin(
            async () =>
            {
                var results = await scheduler
                    .ScheduleManyAsync<
                        IStressTestTrain,
                        StressTestInput,
                        Core.Functional.Unit,
                        int
                    >(
                        Enumerable.Range(0, 5000),
                        i =>
                            (
                                string.Create(CultureInfo.InvariantCulture, $"bulk-{i}"),
                                new StressTestInput
                                {
                                    Value = string.Create(
                                        CultureInfo.InvariantCulture,
                                        $"item-{i}"
                                    ),
                                }
                            ),
                        Every.Minutes(5),
                        options => options.PrunePrefix("bulk-")
                    )
                    .ConfigureAwait(false);

                results.Should().HaveCount(5000);
            },
            TimeSpan.FromSeconds(30) // Larger timeout for bulk operations
        );

        await TestContext
            .Out.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"ScheduleMany 5K with prune: {elapsed.TotalMilliseconds:F0}ms"
                )
            )
            .ConfigureAwait(false);

        // Verify all manifests exist
        DataContext.Reset();
        var count = await DataContext
            .Manifests.CountAsync(m => m.ExternalId.StartsWith("bulk-"))
            .ConfigureAwait(false);
        count.Should().Be(5000);
    }

    [Test]
    public async Task ScheduleMany_SecondRun_PrunesStaleItems_CompletesWithinTimeout()
    {
        // Schedule 1000 items, then re-schedule with 900 (prune 100).
        // Verifies pruning at scale after data exists.
        var scheduler = Scope.ServiceProvider.GetRequiredService<ITraxScheduler>();

        // First run
        await scheduler.ScheduleManyAsync<
            IStressTestTrain,
            StressTestInput,
            Trax.Core.Functional.Unit,
            int
        >(
            Enumerable.Range(0, 1000),
            i =>
                (
                    string.Create(CultureInfo.InvariantCulture, $"evolve-{i}"),
                    new StressTestInput
                    {
                        Value = string.Create(CultureInfo.InvariantCulture, $"v1-{i}"),
                    }
                ),
            Every.Minutes(5),
            options => options.PrunePrefix("evolve-")
        );

        // Second run: keep 0-899, drop 900-999
        var elapsed = await AssertCompletesWithin(
            async () =>
            {
                await scheduler
                    .ScheduleManyAsync<
                        IStressTestTrain,
                        StressTestInput,
                        Core.Functional.Unit,
                        int
                    >(
                        Enumerable.Range(0, 900),
                        i =>
                            (
                                string.Create(CultureInfo.InvariantCulture, $"evolve-{i}"),
                                new StressTestInput
                                {
                                    Value = string.Create(CultureInfo.InvariantCulture, $"v2-{i}"),
                                }
                            ),
                        Every.Minutes(5),
                        options => options.PrunePrefix("evolve-")
                    )
                    .ConfigureAwait(false);
            },
            TimeSpan.FromSeconds(15)
        );

        await TestContext
            .Out.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"ScheduleMany re-run with prune: {elapsed.TotalMilliseconds:F0}ms"
                )
            )
            .ConfigureAwait(false);

        DataContext.Reset();
        var remaining = await DataContext
            .Manifests.CountAsync(m => m.ExternalId.StartsWith("evolve-"))
            .ConfigureAwait(false);
        remaining.Should().Be(900);
    }

    #endregion

    #region Concurrent Worker Dequeue

    [Test]
    public async Task ConcurrentWorkerDequeue_20Workers_NoDeadlocks()
    {
        // Simulates 20 workers competing to claim jobs from background_job.
        // Uses FOR UPDATE SKIP LOCKED — should never deadlock.
        // Seed background jobs
        await SeedBackgroundJobs(100);

        var claimed = new System.Collections.Concurrent.ConcurrentBag<long>();
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();

        var tasks = Enumerable
            .Range(0, 20)
            .Select(async workerId =>
            {
                for (int attempt = 0; attempt < 10; attempt++)
                {
                    var ctx = (IDataContext)factory.Create();
                    await using (ctx.ConfigureAwait(false))
                    {
                        using var tx = await ctx.BeginTransaction().ConfigureAwait(false);

                        var job = await ctx
                            .BackgroundJobs.FromSqlRaw(
                                """
                                SELECT * FROM trax.background_job
                                WHERE fetched_at IS NULL
                                ORDER BY created_at ASC
                                LIMIT 1
                                FOR UPDATE SKIP LOCKED
                                """
                            )
                            .FirstOrDefaultAsync()
                            .ConfigureAwait(false);

                        if (job is not null)
                        {
                            job.FetchedAt = DateTime.UtcNow;
                            await ctx.SaveChanges(CancellationToken.None).ConfigureAwait(false);
                            await ctx.CommitTransaction().ConfigureAwait(false);
                            claimed.Add(job.Id);
                        }
                        else
                        {
                            await ctx.RollbackTransaction().ConfigureAwait(false);
                            break; // No more jobs
                        }
                    }
                }
            })
            .ToArray();

        var elapsed = await AssertCompletesWithin(
            () => Task.WhenAll(tasks),
            TimeSpan.FromSeconds(15)
        );

        // Verify no duplicate claims
        var claimedList = claimed.ToList();
        claimedList
            .Should()
            .OnlyHaveUniqueItems("FOR UPDATE SKIP LOCKED should prevent duplicates");
        claimedList.Count.Should().Be(100, "all 100 jobs should be claimed");

        await TestContext
            .Out.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"ConcurrentDequeue (20 workers, 100 jobs): {elapsed.TotalMilliseconds:F0}ms"
                )
            )
            .ConfigureAwait(false);
    }

    #endregion

    #region Worker Batch Claim Throughput

    [Test]
    public async Task BatchClaim_SingleJob_ClaimsOneAtATime()
    {
        // Baseline: claim 1 job per query (current default behavior)
        await SeedBackgroundJobs(50);

        var claimed = new List<long>();
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();

        var elapsed = await AssertCompletesWithin(
            async () =>
            {
                for (int i = 0; i < 50; i++)
                {
                    var ctx = (IDataContext)factory.Create();
                    await using (ctx.ConfigureAwait(false))
                    {
                        using var tx = await ctx.BeginTransaction().ConfigureAwait(false);

                        var job = await ctx
                            .BackgroundJobs.FromSqlRaw(
                                """
                                SELECT * FROM trax.background_job
                                WHERE fetched_at IS NULL
                                ORDER BY created_at ASC
                                LIMIT 1
                                FOR UPDATE SKIP LOCKED
                                """
                            )
                            .FirstOrDefaultAsync()
                            .ConfigureAwait(false);

                        if (job is not null)
                        {
                            job.FetchedAt = DateTime.UtcNow;
                            await ctx.SaveChanges(CancellationToken.None).ConfigureAwait(false);
                            await ctx.CommitTransaction().ConfigureAwait(false);
                            claimed.Add(job.Id);
                        }
                        else
                        {
                            await ctx.RollbackTransaction().ConfigureAwait(false);
                            break;
                        }
                    }
                }
            },
            TimeSpan.FromSeconds(15)
        );

        claimed.Should().HaveCount(50);
        claimed.Should().OnlyHaveUniqueItems();
        await TestContext
            .Out.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"BatchClaim (LIMIT 1, 50 rounds): {elapsed.TotalMilliseconds:F0}ms"
                )
            )
            .ConfigureAwait(false);
    }

    [Test]
    public async Task BatchClaim_MultiplePer_ClaimsBatchAtOnce()
    {
        // Batch claim: claim 10 jobs per query
        await SeedBackgroundJobs(50);

        var claimed = new List<long>();
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();

        var elapsed = await AssertCompletesWithin(
            async () =>
            {
                for (int i = 0; i < 5; i++) // 5 rounds × 10 per batch = 50 jobs
                {
                    var ctx = (IDataContext)factory.Create();
                    await using (ctx.ConfigureAwait(false))
                    {
                        using var tx = await ctx.BeginTransaction().ConfigureAwait(false);

                        var jobs = await ctx
                            .BackgroundJobs.FromSqlRaw(
                                """
                                SELECT * FROM trax.background_job
                                WHERE fetched_at IS NULL
                                ORDER BY created_at ASC
                                LIMIT 10
                                FOR UPDATE SKIP LOCKED
                                """
                            )
                            .ToListAsync()
                            .ConfigureAwait(false);

                        if (jobs.Count > 0)
                        {
                            foreach (var job in jobs)
                                job.FetchedAt = DateTime.UtcNow;
                            await ctx.SaveChanges(CancellationToken.None).ConfigureAwait(false);
                            await ctx.CommitTransaction().ConfigureAwait(false);
                            claimed.AddRange(jobs.Select(j => j.Id));
                        }
                        else
                        {
                            await ctx.RollbackTransaction().ConfigureAwait(false);
                            break;
                        }
                    }
                }
            },
            TimeSpan.FromSeconds(15)
        );

        claimed.Should().HaveCount(50);
        claimed.Should().OnlyHaveUniqueItems();
        await TestContext
            .Out.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"BatchClaim (LIMIT 10, 5 rounds): {elapsed.TotalMilliseconds:F0}ms"
                )
            )
            .ConfigureAwait(false);
    }

    #endregion

    #region Orphan Manifest Pruning at Scale

    [Test]
    public async Task PruneOrphanedManifests_ServerSideFilter_CompletesWithinTimeout()
    {
        // Simulates SchedulerStartupService.PruneOrphanedManifestsAsync:
        // 1. Server compute: load all (id, external_id) pairs, compute orphan set in C#
        // 2. Database compute: delete orphans in batches by integer PK
        //
        // This avoids the NOT IN(...) clause with many string parameters that caused
        // command timeouts on low-resource Postgres instances.
        var expectedIds = _manifests
            .NotNull()
            .Take(50)
            .Select(m => m.ExternalId)
            .ToHashSet(StringComparer.Ordinal);
        var batchSize = SchedulerStartupService.PruneBatchSize;

        var elapsed = await AssertCompletesWithin(
            async () =>
            {
                // Server compute: lightweight projection, filter in C#
                var allManifests = await DataContext
                    .Manifests.Select(m => new { m.Id, m.ExternalId })
                    .ToListAsync()
                    .ConfigureAwait(false);

                var orphanedIds = allManifests
                    .Where(m => !expectedIds.Contains(m.ExternalId))
                    .Select(m => m.Id)
                    .ToList();

                orphanedIds.Should().HaveCount(ManifestCount - 50);

                // Database compute: delete in batches by integer PK
                var totalPruned = 0;

                foreach (var batch in orphanedIds.Chunk(batchSize))
                {
                    var batchIds = batch.ToList();

                    await DataContext
                        .Manifests.Where(m =>
                            m.DependsOnManifestId.HasValue
                            && batchIds.Contains(m.DependsOnManifestId.Value)
                        )
                        .ExecuteUpdateAsync(s =>
                            s.SetProperty(m => m.DependsOnManifestId, (long?)null)
                        )
                        .ConfigureAwait(false);

                    await DataContext
                        .WorkQueues.Where(w =>
                            w.ManifestId.HasValue && batchIds.Contains(w.ManifestId.Value)
                        )
                        .ExecuteDeleteAsync()
                        .ConfigureAwait(false);

                    await DataContext
                        .DeadLetters.Where(d => batchIds.Contains(d.ManifestId))
                        .ExecuteDeleteAsync()
                        .ConfigureAwait(false);

                    await DataContext
                        .Metadatas.Where(m =>
                            m.ManifestId.HasValue && batchIds.Contains(m.ManifestId.Value)
                        )
                        .ExecuteDeleteAsync()
                        .ConfigureAwait(false);

                    totalPruned += await DataContext
                        .Manifests.Where(m => batchIds.Contains(m.Id))
                        .ExecuteDeleteAsync()
                        .ConfigureAwait(false);
                }

                totalPruned.Should().Be(ManifestCount - 50);
            },
            TimeSpan.FromSeconds(60)
        );

        await TestContext
            .Out.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"PruneOrphanedManifests server-side filter ({ManifestCount - 50} pruned, batch size {batchSize}): {elapsed.TotalMilliseconds:F0}ms"
                )
            )
            .ConfigureAwait(false);
    }

    [Test]
    public async Task PruneOrphanedManifests_LargeExpectedSet_CompletesWithinTimeout()
    {
        // Simulates a production scenario: 450 expected manifests (large
        // expected set), 50 orphans to prune. The previous NOT IN('id1', ..., 'id450')
        // approach would generate a massive SQL statement. The server-side filter loads
        // all 500 ID pairs (~15KB) and computes the diff in C# with O(n) HashSet lookups.
        var expectedIds = _manifests
            .NotNull()
            .Take(ManifestCount - 50)
            .Select(m => m.ExternalId)
            .ToHashSet(StringComparer.Ordinal);
        var batchSize = SchedulerStartupService.PruneBatchSize;

        var elapsed = await AssertCompletesWithin(
            async () =>
            {
                var allManifests = await DataContext
                    .Manifests.Select(m => new { m.Id, m.ExternalId })
                    .ToListAsync()
                    .ConfigureAwait(false);

                var orphanedIds = allManifests
                    .Where(m => !expectedIds.Contains(m.ExternalId))
                    .Select(m => m.Id)
                    .ToList();

                orphanedIds.Should().HaveCount(50);

                var totalPruned = 0;

                foreach (var batch in orphanedIds.Chunk(batchSize))
                {
                    var batchIds = batch.ToList();

                    await DataContext
                        .Manifests.Where(m =>
                            m.DependsOnManifestId.HasValue
                            && batchIds.Contains(m.DependsOnManifestId.Value)
                        )
                        .ExecuteUpdateAsync(s =>
                            s.SetProperty(m => m.DependsOnManifestId, (long?)null)
                        )
                        .ConfigureAwait(false);

                    await DataContext
                        .WorkQueues.Where(w =>
                            w.ManifestId.HasValue && batchIds.Contains(w.ManifestId.Value)
                        )
                        .ExecuteDeleteAsync()
                        .ConfigureAwait(false);

                    await DataContext
                        .DeadLetters.Where(d => batchIds.Contains(d.ManifestId))
                        .ExecuteDeleteAsync()
                        .ConfigureAwait(false);

                    await DataContext
                        .Metadatas.Where(m =>
                            m.ManifestId.HasValue && batchIds.Contains(m.ManifestId.Value)
                        )
                        .ExecuteDeleteAsync()
                        .ConfigureAwait(false);

                    totalPruned += await DataContext
                        .Manifests.Where(m => batchIds.Contains(m.Id))
                        .ExecuteDeleteAsync()
                        .ConfigureAwait(false);
                }

                totalPruned.Should().Be(50);
            },
            TimeSpan.FromSeconds(60)
        );

        await TestContext
            .Out.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"PruneOrphanedManifests large expected set ({ManifestCount - 50} expected, 50 pruned): {elapsed.TotalMilliseconds:F0}ms"
                )
            )
            .ConfigureAwait(false);
    }

    [Test]
    public async Task PruneOrphanedManifests_5KOrphans_CompletesWithinTimeout()
    {
        // Simulates a production scenario at full scale: 5000 orphaned
        // manifests to prune with 500 expected manifests to keep. This is the exact
        // scenario that caused command timeouts — the old NOT IN(...) approach would
        // inline 500 string parameters into the query, which Postgres struggled to plan
        // on a 2 vCPU instance. The server-side filter loads all 5500 ID pairs (~165KB)
        // and computes the diff in C#, then deletes in 10 batches of 500 integer PKs.
        const int orphanCount = 5000;

        // Seed 5000 additional orphan manifests (baseline 500 are the "expected" set)
        var orphanGroup = await SeedManifestGroup("orphan-5k-group");
        var orphans = new List<Manifest>(orphanCount);
        for (var i = 0; i < orphanCount; i++)
        {
            var manifest = Manifest.Create(
                new CreateManifest
                {
                    Name = typeof(StressTestTrain),
                    IsEnabled = true,
                    ScheduleType = ScheduleType.Interval,
                    IntervalSeconds = 60,
                    MaxRetries = 3,
                    Properties = new StressTestInput
                    {
                        Value = string.Create(CultureInfo.InvariantCulture, $"orphan-{i}"),
                    },
                }
            );
            manifest.ExternalId = string.Create(CultureInfo.InvariantCulture, $"orphan-{i}");
            manifest.ManifestGroupId = orphanGroup.Id;
            await DataContext.Track(manifest);
            orphans.Add(manifest);
        }
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        await TestContext
            .Out.WriteLineAsync(
                $"Seeded {orphanCount} orphan manifests (total: {ManifestCount + orphanCount})"
            )
            .ConfigureAwait(false);

        // The 500 baseline manifests (stress-0..stress-499) are the expected set
        var expectedIds = _manifests
            .NotNull()
            .Select(m => m.ExternalId)
            .ToHashSet(StringComparer.Ordinal);
        var batchSize = SchedulerStartupService.PruneBatchSize;

        var elapsed = await AssertCompletesWithin(
                async () =>
                {
                    // Server compute: load all (id, external_id) pairs, filter in C#
                    var allManifests = await DataContext
                        .Manifests.Select(m => new { m.Id, m.ExternalId })
                        .ToListAsync()
                        .ConfigureAwait(false);

                    allManifests.Should().HaveCount(ManifestCount + orphanCount);

                    var orphanedIds = allManifests
                        .Where(m => !expectedIds.Contains(m.ExternalId))
                        .Select(m => m.Id)
                        .ToList();

                    orphanedIds.Should().HaveCount(orphanCount);

                    // Database compute: delete in batches of 500 by integer PK
                    var totalPruned = 0;

                    foreach (var batch in orphanedIds.Chunk(batchSize))
                    {
                        var batchIds = batch.ToList();

                        await DataContext
                            .Manifests.Where(m =>
                                m.DependsOnManifestId.HasValue
                                && batchIds.Contains(m.DependsOnManifestId.Value)
                            )
                            .ExecuteUpdateAsync(s =>
                                s.SetProperty(m => m.DependsOnManifestId, (long?)null)
                            )
                            .ConfigureAwait(false);

                        await DataContext
                            .WorkQueues.Where(w =>
                                w.ManifestId.HasValue && batchIds.Contains(w.ManifestId.Value)
                            )
                            .ExecuteDeleteAsync()
                            .ConfigureAwait(false);

                        await DataContext
                            .DeadLetters.Where(d => batchIds.Contains(d.ManifestId))
                            .ExecuteDeleteAsync()
                            .ConfigureAwait(false);

                        await DataContext
                            .Metadatas.Where(m =>
                                m.ManifestId.HasValue && batchIds.Contains(m.ManifestId.Value)
                            )
                            .ExecuteDeleteAsync()
                            .ConfigureAwait(false);

                        totalPruned += await DataContext
                            .Manifests.Where(m => batchIds.Contains(m.Id))
                            .ExecuteDeleteAsync()
                            .ConfigureAwait(false);
                    }

                    totalPruned.Should().Be(orphanCount);

                    // Verify expected manifests are untouched
                    var remainingCount = await DataContext
                        .Manifests.CountAsync()
                        .ConfigureAwait(false);
                    remainingCount.Should().Be(ManifestCount);
                },
                TimeSpan.FromSeconds(120)
            )
            .ConfigureAwait(false);

        await TestContext
            .Out.WriteLineAsync(
                $"PruneOrphanedManifests 5K orphans ({orphanCount} pruned, {ManifestCount} kept, "
                    + string.Create(
                        CultureInfo.InvariantCulture,
                        $"batch size {batchSize}, {(orphanCount + batchSize - 1) / batchSize} batches): "
                    )
                    + string.Create(
                        CultureInfo.InvariantCulture,
                        $"{elapsed.TotalMilliseconds:F0}ms"
                    )
            )
            .ConfigureAwait(false);
    }

    #endregion

    #region Keyset vs Offset Pagination

    [Test]
    public async Task KeysetPagination_DeepPage_FasterThanOffset()
    {
        // Compare keyset (WHERE id < cursor) vs offset (SKIP N) for deep pages.
        // With 50K metadata rows, offset pagination to page 500 is O(offset).
        var pageSize = 25;
        var targetPage = 200; // page 200 = SKIP 5000

        // First, get a cursor for the deep page using offset
        var cursorRow = await DataContext
            .Metadatas.AsNoTracking()
            .OrderByDescending(m => m.Id)
            .Skip(targetPage * pageSize)
            .Take(1)
            .Select(m => m.Id)
            .FirstOrDefaultAsync();

        cursorRow
            .Should()
            .BeGreaterThan(0, "test data should have enough rows for deep pagination");

        // Measure offset pagination
        var offsetElapsed = await AssertCompletesWithin(async () =>
        {
            var page = await DataContext
                .Metadatas.AsNoTracking()
                .OrderByDescending(m => m.Id)
                .Skip(targetPage * pageSize)
                .Take(pageSize)
                .ToListAsync()
                .ConfigureAwait(false);

            page.Should().HaveCount(pageSize);
        });

        // Measure keyset pagination
        var keysetElapsed = await AssertCompletesWithin(async () =>
        {
            var page = await DataContext
                .Metadatas.AsNoTracking()
                .Where(m => m.Id < cursorRow)
                .OrderByDescending(m => m.Id)
                .Take(pageSize)
                .ToListAsync()
                .ConfigureAwait(false);

            page.Should().HaveCount(pageSize);
        });

        await TestContext
            .Out.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Offset (page {targetPage}): {offsetElapsed.TotalMilliseconds:F0}ms, Keyset: {keysetElapsed.TotalMilliseconds:F0}ms"
                )
            )
            .ConfigureAwait(false);
    }

    [Test]
    public async Task EstimatedCount_LargeTable_SubMillisecond()
    {
        // Verify that querying reltuples from pg_class is near-instant
        var elapsed = await AssertCompletesWithin(
            async () =>
            {
                var connection = (
                    (Microsoft.EntityFrameworkCore.DbContext)DataContext
                ).Database.GetDbConnection();

                if (connection.State != System.Data.ConnectionState.Open)
                    await connection.OpenAsync().ConfigureAwait(false);

                var command = connection.CreateCommand();
                await using (command.ConfigureAwait(false))
                {
                    command.CommandText =
                        "SELECT reltuples::bigint FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE c.relname = 'metadata' AND n.nspname = 'trax'";

                    var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
                    result.Should().NotBeNull();
                }
            },
            TimeSpan.FromMilliseconds(100)
        );

        await TestContext
            .Out.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"EstimatedCount (reltuples): {elapsed.TotalMilliseconds:F2}ms"
                )
            )
            .ConfigureAwait(false);
    }

    #endregion

    #region LoadQueuedJobs with LIMIT

    [Test]
    public async Task LoadQueuedJobs_WithLimit_LoadsOnlyLimitedEntries()
    {
        // Seed 500 queued work queue entries (one per manifest, unique constraint)
        await SeedWorkQueues(_manifests.NotNull());

        var elapsed = await AssertCompletesWithin(async () =>
        {
            var limited = await DataContext
                .WorkQueues.AsNoTracking()
                .Include(
                    $"{nameof(Trax.Effect.Models.WorkQueue.WorkQueue.Manifest)}.{nameof(Trax.Effect.Models.Manifest.Manifest.ManifestGroup)}"
                )
                .Where(q => q.Status == WorkQueueStatus.Queued)
                .Where(q =>
                    q.ManifestId == null
                    || (
                        q.Manifest != null
                        && q.Manifest.ManifestGroup != null
                        && q.Manifest.ManifestGroup.IsEnabled
                    )
                )
                .Where(q => q.ScheduledAt == null || q.ScheduledAt <= DateTime.UtcNow)
                .OrderByDescending(q =>
                    q.Manifest != null && q.Manifest.ManifestGroup != null
                        ? q.Manifest.ManifestGroup.Priority
                        : 0
                )
                .ThenByDescending(q => q.Priority)
                .ThenBy(q => q.CreatedAt)
                .Take(100)
                .ToListAsync()
                .ConfigureAwait(false);

            limited.Should().HaveCount(100);
        });

        await TestContext
            .Out.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"LoadQueuedJobs with LIMIT 100: {elapsed.TotalMilliseconds:F0}ms"
                )
            )
            .ConfigureAwait(false);
    }

    [Test]
    public async Task LoadQueuedJobs_WithoutLimit_LoadsAllEntries()
    {
        // Seed 500 queued work queue entries
        await SeedWorkQueues(_manifests.NotNull());

        var elapsed = await AssertCompletesWithin(async () =>
        {
            var all = await DataContext
                .WorkQueues.AsNoTracking()
                .Include(
                    $"{nameof(Trax.Effect.Models.WorkQueue.WorkQueue.Manifest)}.{nameof(Trax.Effect.Models.Manifest.Manifest.ManifestGroup)}"
                )
                .Where(q => q.Status == WorkQueueStatus.Queued)
                .Where(q =>
                    q.ManifestId == null
                    || (
                        q.Manifest != null
                        && q.Manifest.ManifestGroup != null
                        && q.Manifest.ManifestGroup.IsEnabled
                    )
                )
                .Where(q => q.ScheduledAt == null || q.ScheduledAt <= DateTime.UtcNow)
                .OrderByDescending(q =>
                    q.Manifest != null && q.Manifest.ManifestGroup != null
                        ? q.Manifest.ManifestGroup.Priority
                        : 0
                )
                .ThenByDescending(q => q.Priority)
                .ThenBy(q => q.CreatedAt)
                .ToListAsync()
                .ConfigureAwait(false);

            all.Should().HaveCount(ManifestCount);
        });

        await TestContext
            .Out.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"LoadQueuedJobs without LIMIT ({ManifestCount} entries): {elapsed.TotalMilliseconds:F0}ms"
                )
            )
            .ConfigureAwait(false);
    }

    #endregion

    #region Bulk Update Performance

    [Test]
    public async Task BulkUpdateMetadataState_With50KRows_CompletesWithinTimeout()
    {
        // ExecuteUpdateAsync on many rows — used by ReapStalePendingMetadataJunction and
        // CancelTimedOutJobsJunction to transition state in bulk.
        var now = DateTime.UtcNow;

        var pendingIds = await DataContext
            .Metadatas.Where(m => m.TrainState == TrainState.Pending)
            .Select(m => m.Id)
            .ToListAsync();

        pendingIds.Should().NotBeEmpty();

        var elapsed = await AssertCompletesWithin(async () =>
        {
            await DataContext
                .Metadatas.Where(m =>
                    pendingIds.Contains(m.Id) && m.TrainState == TrainState.Pending
                )
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(m => m.TrainState, TrainState.Failed)
                        .SetProperty(m => m.EndTime, now)
                )
                .ConfigureAwait(false);
        });

        await TestContext
            .Out.WriteLineAsync(
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"BulkUpdateState ({pendingIds.Count} rows): {elapsed.TotalMilliseconds:F0}ms"
                )
            )
            .ConfigureAwait(false);
    }

    #endregion
}
