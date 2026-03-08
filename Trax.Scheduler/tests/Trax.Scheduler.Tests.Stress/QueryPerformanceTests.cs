using System.Diagnostics;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
using Trax.Scheduler.Tests.Stress.Examples;

namespace Trax.Scheduler.Tests.Stress;

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
public class QueryPerformanceTests : StressTestSetup
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

    private List<Manifest> _manifests = null!;
    private ManifestGroup _group = null!;

    public override async Task TestSetUp()
    {
        await base.TestSetUp();

        // Seed baseline data: 500 manifests × 100 metadata = 50K metadata rows
        _group = await SeedManifestGroup("stress-baseline");
        _manifests = await SeedManifests(ManifestCount, _group.Id);

        // Mix of states: 70% completed, 20% failed, 5% in-progress, 5% pending
        var completed = _manifests.Take(350).ToList();
        var failed = _manifests.Skip(350).Take(100).ToList();
        var inProgress = _manifests.Skip(450).Take(25).ToList();
        var pending = _manifests.Skip(475).Take(25).ToList();

        await SeedMetadata(completed, MetadataPerManifest, TrainState.Completed);
        await SeedMetadata(failed, MetadataPerManifest, TrainState.Failed);
        await SeedMetadata(
            inProgress,
            MetadataPerManifest,
            TrainState.InProgress,
            DateTime.UtcNow.AddMinutes(-30)
        );
        await SeedMetadata(
            pending,
            MetadataPerManifest,
            TrainState.Pending,
            DateTime.UtcNow.AddMinutes(-10)
        );

        TestContext.Out.WriteLine(
            $"Seeded {ManifestCount} manifests, {ManifestCount * MetadataPerManifest} metadata rows"
        );
    }

    #region Stale Job Detection (ReapStalePendingMetadataStep pattern)

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
                .ToListAsync();

            stale.Should().NotBeEmpty("test data includes pending metadata older than cutoff");
        });

        TestContext.Out.WriteLine($"FindStalePendingMetadata: {elapsed.TotalMilliseconds:F0}ms");
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
                .ToListAsync();

            stuck.Should().NotBeEmpty("test data includes in-progress metadata");
        });

        TestContext.Out.WriteLine($"RecoverStuckJobs: {elapsed.TotalMilliseconds:F0}ms");
    }

    #endregion

    #region Dispatch Capacity (LoadDispatchCapacityStep pattern)

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
                .ToListAsync();

            activeCounts.Should().NotBeEmpty("test data includes active metadata");
        });

        TestContext.Out.WriteLine($"LoadDispatchCapacity: {elapsed.TotalMilliseconds:F0}ms");
    }

    #endregion

    #region Cancel Timed-Out Jobs (CancelTimedOutJobsStep pattern)

    [Test]
    public async Task FindTimedOutJobs_With50KRows_CompletesWithinTimeout()
    {
        // Query: WHERE manifest_id IN (...) AND train_state='in_progress' AND !cancellation_requested
        // Needs composite index on (manifest_id, train_state) for efficient filtering.
        var manifestIds = _manifests.Select(m => m.Id).ToList();

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
                .ToListAsync();

            inProgress.Should().NotBeEmpty();
        });

        TestContext.Out.WriteLine($"FindTimedOutJobs: {elapsed.TotalMilliseconds:F0}ms");
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
                .ToListAsync();

            counts.Should().NotBeEmpty();
        });

        TestContext.Out.WriteLine($"DashboardDailyCount: {elapsed.TotalMilliseconds:F0}ms");
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
                .ToListAsync();

            page.Should().HaveCount(50);
        });

        TestContext.Out.WriteLine($"PaginatedExecutions: {elapsed.TotalMilliseconds:F0}ms");
    }

    #endregion

    #region LoadManifestsStep Pattern (Subquery aggregation)

    [Test]
    public async Task LoadManifestsWithAggregates_With50KMetadata_CompletesWithinTimeout()
    {
        // LoadManifestsStep: SELECT manifests + COUNT/EXISTS subqueries on child tables
        // With 500 manifests and 50K metadata, subqueries run 500 times.
        await SeedWorkQueues(_manifests.Take(50).ToList());

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
                .ToListAsync();

            views.Should().HaveCount(ManifestCount);
        });

        TestContext.Out.WriteLine($"LoadManifestsWithAggregates: {elapsed.TotalMilliseconds:F0}ms");
    }

    #endregion

    #region Metadata Cleanup (DeleteExpiredMetadataStep pattern)

    [Test]
    public async Task DeleteExpiredMetadata_With50KRows_CompletesWithinTimeout()
    {
        // DeleteExpiredMetadataStep: DELETE WHERE name IN (whitelist) AND start_time < cutoff
        // AND train_state IN (completed, failed, cancelled). Uses subquery for cascade.
        var trainName = typeof(StressTestTrain).FullName!;
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
                    .ExecuteDeleteAsync();

                // Delete the metadata itself
                var deleted = await DataContext
                    .Metadatas.Where(m => m.Name == trainName)
                    .Where(m => m.StartTime < cutoff)
                    .Where(m =>
                        m.TrainState == TrainState.Completed
                        || m.TrainState == TrainState.Failed
                        || m.TrainState == TrainState.Cancelled
                    )
                    .ExecuteDeleteAsync();

                deleted.Should().BeGreaterThan(0, "expired metadata should exist in test data");
            },
            TimeSpan.FromSeconds(30)
        );

        TestContext.Out.WriteLine($"DeleteExpiredMetadata: {elapsed.TotalMilliseconds:F0}ms");
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
        await SeedWorkQueues(_manifests.Take(100).ToList());
        await SeedDeadLetters(_manifests.Take(50).ToList(), 1);

        // Keep 400 manifests, prune 100
        var keepIds = _manifests.Take(400).Select(m => m.ExternalId).ToHashSet();

        var elapsed = await AssertCompletesWithin(
            async () =>
            {
                var staleManifestIds = await DataContext
                    .Manifests.Where(m =>
                        m.ExternalId.StartsWith("stress-") && !keepIds.Contains(m.ExternalId)
                    )
                    .Select(m => m.Id)
                    .ToListAsync();

                staleManifestIds.Should().HaveCount(100);

                await DataContext
                    .WorkQueues.Where(w =>
                        w.ManifestId.HasValue && staleManifestIds.Contains(w.ManifestId.Value)
                    )
                    .ExecuteDeleteAsync();

                await DataContext
                    .DeadLetters.Where(d => staleManifestIds.Contains(d.ManifestId))
                    .ExecuteDeleteAsync();

                await DataContext
                    .Metadatas.Where(m =>
                        m.ManifestId.HasValue && staleManifestIds.Contains(m.ManifestId.Value)
                    )
                    .ExecuteDeleteAsync();

                await DataContext
                    .Manifests.Where(m => staleManifestIds.Contains(m.Id))
                    .ExecuteDeleteAsync();
            },
            TimeSpan.FromSeconds(30)
        );

        TestContext.Out.WriteLine(
            $"PruneStaleManifests (100 pruned, 400 kept): {elapsed.TotalMilliseconds:F0}ms"
        );
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
                var results = await scheduler.ScheduleManyAsync<
                    IStressTestTrain,
                    StressTestInput,
                    LanguageExt.Unit,
                    int
                >(
                    Enumerable.Range(0, 5000),
                    i => ($"bulk-{i}", new StressTestInput { Value = $"item-{i}" }),
                    Every.Minutes(5),
                    options => options.PrunePrefix("bulk-")
                );

                results.Should().HaveCount(5000);
            },
            TimeSpan.FromSeconds(30) // Larger timeout for bulk operations
        );

        TestContext.Out.WriteLine($"ScheduleMany 5K with prune: {elapsed.TotalMilliseconds:F0}ms");

        // Verify all manifests exist
        DataContext.Reset();
        var count = await DataContext.Manifests.CountAsync(m => m.ExternalId.StartsWith("bulk-"));
        count.Should().Be(5000);
    }

    [Test]
    public async Task ScheduleMany_SecondRun_PrunesStaleItems_CompletesWithinTimeout()
    {
        // Schedule 1000 items, then re-schedule with 900 (prune 100).
        // Verifies pruning at scale after data exists.
        var scheduler = Scope.ServiceProvider.GetRequiredService<ITraxScheduler>();

        // First run
        await scheduler.ScheduleManyAsync<IStressTestTrain, StressTestInput, LanguageExt.Unit, int>(
            Enumerable.Range(0, 1000),
            i => ($"evolve-{i}", new StressTestInput { Value = $"v1-{i}" }),
            Every.Minutes(5),
            options => options.PrunePrefix("evolve-")
        );

        // Second run: keep 0-899, drop 900-999
        var elapsed = await AssertCompletesWithin(
            async () =>
            {
                await scheduler.ScheduleManyAsync<
                    IStressTestTrain,
                    StressTestInput,
                    LanguageExt.Unit,
                    int
                >(
                    Enumerable.Range(0, 900),
                    i => ($"evolve-{i}", new StressTestInput { Value = $"v2-{i}" }),
                    Every.Minutes(5),
                    options => options.PrunePrefix("evolve-")
                );
            },
            TimeSpan.FromSeconds(15)
        );

        TestContext.Out.WriteLine(
            $"ScheduleMany re-run with prune: {elapsed.TotalMilliseconds:F0}ms"
        );

        DataContext.Reset();
        var remaining = await DataContext.Manifests.CountAsync(m =>
            m.ExternalId.StartsWith("evolve-")
        );
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
                    await using var ctx = (IDataContext)factory.Create();
                    using var tx = await ctx.BeginTransaction();

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
                        .FirstOrDefaultAsync();

                    if (job is not null)
                    {
                        job.FetchedAt = DateTime.UtcNow;
                        await ctx.SaveChanges(CancellationToken.None);
                        await ctx.CommitTransaction();
                        claimed.Add(job.Id);
                    }
                    else
                    {
                        await ctx.RollbackTransaction();
                        break; // No more jobs
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

        TestContext.Out.WriteLine(
            $"ConcurrentDequeue (20 workers, 100 jobs): {elapsed.TotalMilliseconds:F0}ms"
        );
    }

    #endregion

    #region Orphan Manifest Pruning at Scale

    [Test]
    public async Task PruneOrphanedManifests_AtStartupScale_CompletesWithinTimeout()
    {
        // Simulates SchedulerStartupService.PruneOrphanedManifestsAsync with many manifests
        // and a small expected set — most manifests are orphaned and need deletion.
        var expectedIds = _manifests.Take(50).Select(m => m.ExternalId).ToHashSet();

        var elapsed = await AssertCompletesWithin(
            async () =>
            {
                var orphanedIds = await DataContext
                    .Manifests.Where(m => !expectedIds.Contains(m.ExternalId))
                    .Select(m => m.Id)
                    .ToListAsync();

                orphanedIds.Should().HaveCount(ManifestCount - 50);

                await DataContext
                    .Manifests.Where(m =>
                        m.DependsOnManifestId.HasValue
                        && orphanedIds.Contains(m.DependsOnManifestId.Value)
                    )
                    .ExecuteUpdateAsync(s =>
                        s.SetProperty(m => m.DependsOnManifestId, (long?)null)
                    );

                await DataContext
                    .WorkQueues.Where(w =>
                        w.ManifestId.HasValue && orphanedIds.Contains(w.ManifestId.Value)
                    )
                    .ExecuteDeleteAsync();

                await DataContext
                    .DeadLetters.Where(d => orphanedIds.Contains(d.ManifestId))
                    .ExecuteDeleteAsync();

                await DataContext
                    .Metadatas.Where(m =>
                        m.ManifestId.HasValue && orphanedIds.Contains(m.ManifestId.Value)
                    )
                    .ExecuteDeleteAsync();

                var pruned = await DataContext
                    .Manifests.Where(m => orphanedIds.Contains(m.Id))
                    .ExecuteDeleteAsync();

                pruned.Should().Be(ManifestCount - 50);
            },
            TimeSpan.FromSeconds(60)
        );

        TestContext.Out.WriteLine(
            $"PruneOrphanedManifests ({ManifestCount - 50} pruned): {elapsed.TotalMilliseconds:F0}ms"
        );
    }

    #endregion

    #region Bulk Update Performance

    [Test]
    public async Task BulkUpdateMetadataState_With50KRows_CompletesWithinTimeout()
    {
        // ExecuteUpdateAsync on many rows — used by ReapStalePendingMetadataStep and
        // CancelTimedOutJobsStep to transition state in bulk.
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
                );
        });

        TestContext.Out.WriteLine(
            $"BulkUpdateState ({pendingIds.Count} rows): {elapsed.TotalMilliseconds:F0}ms"
        );
    }

    #endregion
}
