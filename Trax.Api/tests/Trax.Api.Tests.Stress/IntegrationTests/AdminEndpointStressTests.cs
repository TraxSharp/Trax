using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Mutations;
using Trax.Api.GraphQL.Queries;
using Trax.Api.Services.HealthCheck;
using Trax.Api.Services.Metrics;
using Trax.Api.Tests.Stress.Fixtures;
using Trax.Core.Exceptions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Enums;
using Trax.Effect.Services.ChangeSignal;
using Trax.Effect.Services.EffectRegistry;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Scheduler.Services.Effects;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.Tests.Stress.IntegrationTests;

/// <summary>
/// One SLA test per administrative GraphQL endpoint the React dashboard consumes, each run
/// against millions of rows. The resolver classes are invoked directly (their
/// <c>[Service]</c> parameters resolved from the real DI container) so the measured cost is
/// the exact query the dashboard triggers.
/// </summary>
/// <remarks>
/// Failing a test here is a finding, not flake: it means that endpoint degrades at scale and
/// the dashboard would stall on it. Fixes go in Postgres migrations (indexes) or the query
/// itself, then this suite proves the latency is flat.
/// </remarks>
[TestFixture]
[Category("Stress")]
[Explicit(
    "Stress suite: seeds millions of rows. Run with dotnet test --filter TestCategory=Stress"
)]
public class AdminEndpointStressTests : StressTestSetup
{
    private static IDataContextProviderFactory Factory(IServiceProvider sp) =>
        sp.GetRequiredService<IDataContextProviderFactory>();

    // What HotChocolate injects into the list resolvers: the provider's dialect, whose row
    // estimate stands in for an exact count of an unfiltered table.
    private static ISqlDialect Dialect(IServiceProvider sp) => sp.GetRequiredService<ISqlDialect>();

    private static IOperationsService Operations(IServiceProvider sp) =>
        sp.GetRequiredService<IOperationsService>();

    #region Health / discovery / config

    [Test]
    public async Task Health_AtScale_WithinBudget()
    {
        await MeasureAsync(
            "operations.health",
            HealthBudget,
            async (sp, ct) =>
            {
                var health = await new OperationsQueries().GetHealth(
                    sp.GetRequiredService<ITraxHealthService>(),
                    ct
                );
                health.Should().NotBeNull();
                (health.QueueDepth + health.DeadLetters).Should().BeGreaterThan(0);
            }
        );
    }

    [Test]
    public async Task Trains_AtScale_WithinBudget()
    {
        await MeasureAsync(
            "operations.trains",
            TrivialBudget,
            (sp, _) =>
            {
                var trains = new OperationsQueries().GetTrains(
                    sp.GetRequiredService<ITrainDiscoveryService>()
                );
                trains.Should().NotBeEmpty();
                return Task.CompletedTask;
            }
        );
    }

    [Test]
    public async Task ConfigScheduler_AtScale_WithinBudget()
    {
        await MeasureAsync(
            "operations.config.scheduler",
            TrivialBudget,
            (sp, _) =>
            {
                var config = new ConfigQueries().GetScheduler(Operations(sp));
                config.Should().NotBeNull();
                return Task.CompletedTask;
            }
        );
    }

    [Test]
    public async Task MetricsServer_AtScale_WithinBudget()
    {
        await MeasureAsync(
            "operations.metrics.server",
            TrivialBudget,
            (sp, _) =>
            {
                var server = new MetricsQueries().GetServer(Operations(sp));
                server.UptimeSeconds.Should().BeGreaterThan(0);
                return Task.CompletedTask;
            }
        );
    }

    [Test]
    public async Task Effects_AtScale_WithinBudget()
    {
        await MeasureAsync(
            "operations.effects",
            TrivialBudget,
            (sp, _) =>
            {
                var effects = new OperationsQueries().GetEffects(
                    sp.GetRequiredService<IEffectSettingsService>()
                );
                effects.Should().NotBeNull();
                return Task.CompletedTask;
            }
        );
    }

    [Test]
    public async Task MetricsServerCpuPercent_AtScale_WithinBudget()
    {
        // Stateful per process: the warm-up primes the baseline sample, so the measured poll
        // is the steady-state one the dashboard makes every few seconds.
        await MeasureAsync(
            "operations.metrics.serverCpuPercent",
            TrivialBudget,
            (sp, _) =>
            {
                new MetricsQueries().GetServerCpuPercent(
                    sp.GetRequiredService<ProcessCpuSampler>()
                );
                return Task.CompletedTask;
            }
        );
    }

    #endregion

    #region Point reads (dashboard detail pages)

    [Test]
    public async Task Manifest_PointRead_WithinBudget()
    {
        await MeasureAsync(
            "operations.manifest (by id)",
            ListBudget,
            async (sp, ct) =>
            {
                var row = await new OperationsQueries().GetManifest(
                    Profile.Manifests / 2,
                    Factory(sp),
                    ct
                );
                row.Should().NotBeNull();
            }
        );
    }

    [Test]
    public async Task ManifestGroup_PointRead_WithinBudget()
    {
        await MeasureAsync(
            "operations.manifestGroups.group (by id)",
            ListBudget,
            async (sp, ct) =>
            {
                var row = await new ManifestGroupQueries().GetGroup(
                    Profile.Groups / 2,
                    Factory(sp),
                    ct
                );
                row.Should().NotBeNull();
            }
        );
    }

    [Test]
    public async Task WorkQueue_PointRead_WithinBudget()
    {
        await MeasureAsync(
            "operations.workQueue.workQueue (by id)",
            ListBudget,
            async (sp, ct) =>
            {
                var row = await new WorkQueueQueries().GetWorkQueue(
                    Profile.WorkQueue / 2,
                    Factory(sp),
                    ct
                );
                row.Should().NotBeNull();
            }
        );
    }

    [Test]
    public async Task DeadLetter_PointRead_WithinBudget()
    {
        await MeasureAsync(
            "operations.deadLetters.deadLetter (by id)",
            ListBudget,
            async (sp, ct) =>
            {
                var row = await new DeadLetterQueries().GetDeadLetter(
                    Profile.DeadLetter / 2,
                    Factory(sp),
                    ct
                );
                row.Should().NotBeNull();
            }
        );
    }

    #endregion

    #region Metrics dashboard (the heavy aggregations)

    [Test]
    public async Task MetricsDashboard_Last24Hours_WithinBudget()
    {
        await MeasureAsync(
            "operations.metrics.dashboard (24h)",
            MetricsBudget,
            async (sp, ct) =>
            {
                var metrics = await new MetricsQueries().GetDashboard(
                    Operations(sp),
                    ct,
                    MetricsRange.Last24Hours
                );
                metrics.Kpis.ExecutionsToday.Should().BeGreaterThan(0);
                metrics.ExecutionsOverTime.Should().HaveCount(24);
            }
        );
    }

    [Test]
    public async Task MetricsDashboard_Last60Minutes_WithinBudget()
    {
        await MeasureAsync(
            "operations.metrics.dashboard (60m)",
            MetricsBudget,
            async (sp, ct) =>
            {
                var metrics = await new MetricsQueries().GetDashboard(
                    Operations(sp),
                    ct,
                    MetricsRange.Last60Minutes
                );
                metrics.ExecutionsOverTime.Should().HaveCount(60);
                metrics.TopFailures.Should().NotBeEmpty();
            }
        );
    }

    [Test]
    public async Task MetricsDashboard_HideAdminTrains_WithinBudget()
    {
        await MeasureAsync(
            "operations.metrics.dashboard (hideAdmin)",
            MetricsBudget,
            async (sp, ct) =>
            {
                var metrics = await new MetricsQueries().GetDashboard(
                    Operations(sp),
                    ct,
                    MetricsRange.Last24Hours,
                    hideAdminTrains: true
                );
                metrics.Should().NotBeNull();
            }
        );
    }

    [Test]
    public async Task Hosts_AtScale_WithinBudget()
    {
        // The cluster rollup is a full aggregation of the metadata table by host instance (no time
        // filter, so it scans every row). Inherently O(rows) and refresh-on-demand, so it gets the
        // dedicated cluster budget, not the hot-path metrics budget. Migration 039's covering index
        // keeps it near the floor for a full aggregation.
        await MeasureAsync(
            "operations.hosts",
            ClusterBudget,
            async (sp, ct) =>
            {
                var hosts = await new OperationsQueries().GetHosts(Factory(sp), ct);
                hosts.Should().NotBeEmpty();
                // Every seeded row carries a host, so the per-host totals account for at least the
                // whole seed (a reused stress DB may hold a few extra rows from other processes).
                hosts
                    .Sum(h => h.TotalExecutions)
                    .Should()
                    .BeGreaterThanOrEqualTo(Profile.Metadata);
            }
        );
    }

    #endregion

    #region Executions (metadata) — pagination

    [Test]
    public async Task Executions_FirstPage_WithinBudget()
    {
        await MeasureAsync(
            "operations.executions (first page)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new OperationsQueries().GetExecutions(
                    Factory(sp),
                    ct,
                    take: 25,
                    sqlDialect: Dialect(sp)
                );
                page.Items.Should().HaveCount(25);
                page.TotalCount.Should().BeGreaterThan(0);
            }
        );
    }

    [Test]
    public async Task Executions_KeysetDeep_WithinBudget()
    {
        await MeasureAsync(
            "operations.executions (keyset, far end)",
            ListBudget,
            async (sp, ct) =>
            {
                // Cursor near the end of the id-DESC sequence: a keyset seek is O(page)
                // no matter how deep the cursor is.
                var page = await new OperationsQueries().GetExecutions(
                    Factory(sp),
                    ct,
                    take: 25,
                    afterId: 200,
                    sqlDialect: Dialect(sp)
                );
                page.Items.Should().NotBeEmpty();
            }
        );
    }

    [Test]
    public async Task Executions_PointRead_WithinBudget()
    {
        await MeasureAsync(
            "operations.execution (by id)",
            ListBudget,
            async (sp, ct) =>
            {
                var row = await new OperationsQueries().GetExecution(
                    Profile.Metadata / 2,
                    Factory(sp),
                    ct
                );
                row.Should().NotBeNull();
            }
        );
    }

    [Test]
    public async Task Executions_DeepestOffset_WithinBudget_AndDeeperIsRefused()
    {
        // The deepest offset served (Api ADR 0017) stays within a list read's budget at scale;
        // anything deeper is refused with a code that points at the keyset cursor.
        await MeasureAsync(
            $"operations.executions (skip {OperationsPageBounds.MaxSkip:N0})",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new OperationsQueries().GetExecutions(
                    Factory(sp),
                    ct,
                    skip: OperationsPageBounds.MaxSkip,
                    take: 25,
                    sqlDialect: Dialect(sp)
                );
                page.Items.Should().HaveCount(25);
            }
        );

        var deeper = async () =>
            await new OperationsQueries().GetExecutions(
                Factory(Services),
                CancellationToken.None,
                skip: (int)Math.Max(OperationsPageBounds.MaxSkip + 1, Profile.Metadata - 50),
                take: 25,
                sqlDialect: Dialect(Services)
            );

        (await deeper.Should().ThrowAsync<HotChocolate.GraphQLException>())
            .Which.Errors.Should()
            .ContainSingle()
            .Which.Code.Should()
            .Be(
                OperationsPageBounds.SkipTooDeepCode,
                "a deep offset reads every skipped row; the client pages by afterId "
                    + "(docs/adr/0017-an-operations-page-is-at-most-500-rows.md)"
            );
    }

    #endregion

    #region Work queue — pagination + filters

    [Test]
    public async Task WorkQueue_FirstPage_WithinBudget()
    {
        await MeasureAsync(
            "operations.workQueue.workQueues (first page)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new WorkQueueQueries().GetWorkQueues(
                    Factory(sp),
                    ct,
                    take: 25,
                    sqlDialect: Dialect(sp)
                );
                page.Items.Should().HaveCount(25);
                page.TotalCount.Should().BeGreaterThan(0);
            }
        );
    }

    [Test]
    public async Task WorkQueue_FilterByStatus_WithinBudget()
    {
        await MeasureAsync(
            "operations.workQueue.workQueues (status=Dispatched)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new WorkQueueQueries().GetWorkQueues(
                    Factory(sp),
                    ct,
                    take: 25,
                    status: WorkQueueStatus.Dispatched,
                    sqlDialect: Dialect(sp)
                );
                page.Items.Should().NotBeEmpty();
                page.Items.Should().OnlyContain(x => x.Status == WorkQueueStatus.Dispatched);
                page.TotalCount.Should().BeGreaterThan(0);
            }
        );
    }

    [Test]
    public async Task WorkQueue_FilterByTrainName_WithinBudget()
    {
        await MeasureAsync(
            "operations.workQueue.workQueues (trainName)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new WorkQueueQueries().GetWorkQueues(
                    Factory(sp),
                    ct,
                    take: 25,
                    trainName: "Trax.Stress.Trains.IStressTrain7",
                    sqlDialect: Dialect(sp)
                );
                page.Items.Should().NotBeEmpty();
                page.Items.Should()
                    .OnlyContain(x => x.TrainName == "Trax.Stress.Trains.IStressTrain7");
            }
        );
    }

    #endregion

    #region Dead letters — pagination + filters

    [Test]
    public async Task DeadLetters_FirstPage_WithinBudget()
    {
        await MeasureAsync(
            "operations.deadLetters.deadLetters (first page)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new DeadLetterQueries().GetDeadLetters(Factory(sp), ct, take: 25);
                page.Items.Should().HaveCount(25);
                page.TotalCount.Should().BeGreaterThan(0);
            }
        );
    }

    [Test]
    public async Task DeadLetters_FilterByStatus_WithinBudget()
    {
        await MeasureAsync(
            "operations.deadLetters.deadLetters (status=Retried)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new DeadLetterQueries().GetDeadLetters(
                    Factory(sp),
                    ct,
                    take: 25,
                    status: DeadLetterStatus.Retried
                );
                page.Items.Should().NotBeEmpty();
                page.Items.Should().OnlyContain(x => x.Status == DeadLetterStatus.Retried);
            }
        );
    }

    #endregion

    #region Logs — pagination + filters

    [Test]
    public async Task Logs_FirstPage_WithinBudget()
    {
        await MeasureAsync(
            "operations.logs.logs (first page)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new LogQueries().GetLogs(
                    Operations(sp),
                    Factory(sp),
                    ct,
                    take: 25,
                    sqlDialect: Dialect(sp)
                );
                page.Items.Should().HaveCount(25);
                page.TotalCount.Should().BeGreaterThan(0);
            }
        );
    }

    [Test]
    public async Task Logs_FilterByMetadataId_WithinBudget()
    {
        await MeasureAsync(
            "operations.logs.logs (metadataId)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new LogQueries().GetLogs(
                    Operations(sp),
                    Factory(sp),
                    ct,
                    take: 25,
                    metadataId: 1,
                    sqlDialect: Dialect(sp)
                );
                page.Items.Should().OnlyContain(x => x.MetadataId == 1);
            }
        );
    }

    [Test]
    public async Task Logs_FilterByLevel_WithinBudget()
    {
        await MeasureAsync(
            "operations.logs.logs (minimumLevel=Error)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new LogQueries().GetLogs(
                    Operations(sp),
                    Factory(sp),
                    ct,
                    take: 25,
                    minimumLevel: LogLevel.Error,
                    sqlDialect: Dialect(sp)
                );
                page.Items.Should().NotBeEmpty();
                page.Items.Should().OnlyContain(x => x.Level >= LogLevel.Error);
            }
        );
    }

    [Test]
    public async Task Logs_FilterByCategory_WithinBudget()
    {
        await MeasureAsync(
            "operations.logs.logs (category)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new LogQueries().GetLogs(
                    Operations(sp),
                    Factory(sp),
                    ct,
                    take: 25,
                    category: "Trax.Stress.Category3",
                    sqlDialect: Dialect(sp)
                );
                page.Items.Should().NotBeEmpty();
                page.Items.Should().OnlyContain(x => x.Category == "Trax.Stress.Category3");
            }
        );
    }

    #endregion

    #region Manifests + manifest groups

    [Test]
    public async Task Manifests_FirstPage_WithinBudget()
    {
        await MeasureAsync(
            "operations.manifests (first page)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new OperationsQueries().GetManifests(
                    Factory(sp),
                    ct,
                    take: 25,
                    sqlDialect: Dialect(sp)
                );
                page.Items.Should().NotBeEmpty();
            }
        );
    }

    [Test]
    public async Task ManifestGroups_FirstPage_WithinBudget()
    {
        await MeasureAsync(
            "operations.manifestGroups.groups (first page)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new ManifestGroupQueries().GetGroups(
                    Factory(sp),
                    ct,
                    take: 25,
                    sqlDialect: Dialect(sp)
                );
                page.Items.Should().NotBeEmpty();
            }
        );
    }

    [Test]
    public async Task ManifestGroups_DependencyGraph_WithinBudget()
    {
        await MeasureAsync(
            "operations.manifestGroups.graph",
            ListBudget,
            async (sp, ct) =>
            {
                var graph = await new ManifestGroupQueries().GetGraph(1, Operations(sp), ct);
                graph.Should().NotBeNull();
                graph!.Nodes.Should().NotBeEmpty();
            }
        );
    }

    [Test]
    public async Task ManifestGroups_GlobalDependencyGraph_WithinBudget()
    {
        // The whole-graph read: every group as a node plus a self-join over the manifest table for
        // cross-group edges. The manifest table is small relative to metadata, so it stays cheap.
        await MeasureAsync(
            "operations.manifestGroups.dependencyGraph",
            ListBudget,
            async (sp, ct) =>
            {
                var graph = await new ManifestGroupQueries().GetDependencyGraph(Operations(sp), ct);
                graph.Nodes.Should().NotBeEmpty();
            }
        );
    }

    #endregion

    #region Manifest- and group-scoped reads (dashboard detail pages)

    [Test]
    public async Task Executions_FilterByManifestId_WithinBudget()
    {
        await MeasureAsync(
            "operations.executions (manifestId)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new OperationsQueries().GetExecutions(
                    Factory(sp),
                    ct,
                    take: 25,
                    manifestId: 1,
                    sqlDialect: Dialect(sp)
                );
                page.Items.Should().NotBeEmpty();
                page.Items.Should().OnlyContain(e => e.ManifestId == 1);
                page.TotalCount.Should().BeGreaterThan(0);
            }
        );
    }

    [Test]
    public async Task Executions_FilterByManifestGroupId_WithinBudget()
    {
        await MeasureAsync(
            "operations.executions (manifestGroupId)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new OperationsQueries().GetExecutions(
                    Factory(sp),
                    ct,
                    take: 25,
                    manifestGroupId: 1,
                    sqlDialect: Dialect(sp)
                );
                page.Items.Should().NotBeEmpty();
                page.TotalCount.Should().BeGreaterThan(0);
            }
        );
    }

    [Test]
    public async Task Manifests_FilterByManifestGroupId_WithinBudget()
    {
        await MeasureAsync(
            "operations.manifests (manifestGroupId)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new OperationsQueries().GetManifests(
                    Factory(sp),
                    ct,
                    take: 25,
                    manifestGroupId: 1,
                    sqlDialect: Dialect(sp)
                );
                page.Items.Should().NotBeEmpty();
                page.Items.Should().OnlyContain(m => m.ManifestGroupId == 1);
            }
        );
    }

    [Test]
    public async Task ManifestStats_WithinBudget()
    {
        await MeasureAsync(
            "operations.manifestStats",
            ListBudget,
            async (sp, ct) =>
            {
                var stats = await new OperationsQueries().GetManifestStats(1, Operations(sp), ct);
                stats.ManifestId.Should().Be(1);
                stats.Total.Should().BeGreaterThan(0);
            }
        );
    }

    [Test]
    public async Task TrainStats_WithinBudget()
    {
        await MeasureAsync(
            "operations.trainStats",
            ListBudget,
            async (sp, ct) =>
            {
                // One of the seeded train names (name = TrainName || (g % TrainNames)). The
                // ix_metadata_name_train_state index serves the filter + state grouping.
                var stats = await new OperationsQueries().GetTrainStats(
                    "Trax.Stress.Trains.IStressTrain0",
                    Factory(sp),
                    ct
                );
                stats.Total.Should().BeGreaterThan(0);
                stats.Completed.Should().BeGreaterThan(0);
            }
        );
    }

    [Test]
    public async Task GroupStats_VisiblePage_WithinBudget()
    {
        // The dashboard groups list requests stats for its visible page (25 groups). This is the
        // heaviest of the new reads: it joins metadata to each group's manifests and aggregates by
        // state, so it gets the metrics budget rather than the list budget.
        var groupIds = Enumerable.Range(1, 25).Select(i => (long)i).ToArray();
        await MeasureAsync(
            "operations.manifestGroups.stats (25 groups)",
            MetricsBudget,
            async (sp, ct) =>
            {
                var stats = await new ManifestGroupQueries().GetStats(groupIds, Operations(sp), ct);
                stats.Should().HaveCount(25);
                stats.Should().Contain(s => s.TotalExecutions > 0);
            }
        );
    }

    [Test]
    public async Task ManifestExclusions_WithinBudget()
    {
        await MeasureAsync(
            "operations.manifestExclusions",
            TrivialBudget,
            async (sp, ct) =>
            {
                // Seeded manifests carry no exclusions; the point is the single-manifest read +
                // JSON parse stays trivial regardless of metadata volume.
                var windows = await new OperationsQueries().GetManifestExclusions(
                    1,
                    Factory(sp),
                    ct
                );
                windows.Should().NotBeNull();
            }
        );
    }

    #endregion

    #region New query paths (time-range, sort, children) + bulk/single mutations

    [Test]
    public async Task Executions_TimeRange_WithinBudget()
    {
        await MeasureAsync(
            "operations.executions (last 24h)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new OperationsQueries().GetExecutions(
                    Factory(sp),
                    ct,
                    take: 25,
                    startedAfter: DateTime.UtcNow.AddHours(-24),
                    sqlDialect: Dialect(sp)
                );
                page.Items.Should().NotBeEmpty();
                page.TotalCount.Should().BeGreaterThan(0);
            }
        );
    }

    [Test]
    public async Task Executions_OrderOldest_WithinBudget()
    {
        await MeasureAsync(
            "operations.executions (oldest first)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new OperationsQueries().GetExecutions(
                    Factory(sp),
                    ct,
                    take: 25,
                    order: SortOrder.Oldest,
                    sqlDialect: Dialect(sp)
                );
                page.Items.Should().HaveCount(25);
                page.Items.Select(e => e.Id).Should().BeInAscendingOrder();
            }
        );
    }

    [Test]
    public async Task ExecutionChildren_WithinBudget()
    {
        await MeasureAsync(
            "operations.executionChildren",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new OperationsQueries().GetExecutionChildren(1, Factory(sp), ct);
                page.Items.Should().NotBeEmpty();
                page.TotalCount.Should().BeGreaterThan(0);
            }
        );
    }

    [Test]
    public async Task JunctionRuns_WithinBudget()
    {
        // Eight steps for each of the newest 250k runs, ~2M rows, so the read of one run's
        // timeline is measured against a table of real size. Idempotent across runs.
        await ExecSqlAsync(
            """
            INSERT INTO trax.junction_run (metadata_id, position, kind, name, state, started_at, ended_at)
            SELECT m.id, p.position, 'junction', 'StressStep' || p.position, 'completed',
                   m.start_time, m.start_time + interval '5 milliseconds'
            FROM (SELECT id, start_time FROM trax.metadata ORDER BY id DESC LIMIT 250000) m
            CROSS JOIN generate_series(0, 7) AS p(position)
            ON CONFLICT (metadata_id, position) DO NOTHING
            """
        );
        var runId = await ScalarAsync<long>(
            "SELECT max(metadata_id) FROM trax.junction_run WHERE name LIKE 'StressStep%'"
        );

        await MeasureAsync(
            "operations.junctionRuns",
            ListBudget,
            async (sp, ct) =>
            {
                var steps = await new OperationsQueries().GetJunctionRuns(runId, Factory(sp), ct);
                steps.Should().HaveCount(8);
            }
        );
    }

    [Test]
    public async Task ExecutionDetail_WithChildCount_WithinBudget()
    {
        await MeasureAsync(
            "operations.executionDetail (+childCount)",
            ListBudget,
            async (sp, ct) =>
            {
                var detail = await new OperationsQueries().GetExecutionDetail(1, Factory(sp), ct);
                detail.Should().NotBeNull();
                detail!.ChildCount.Should().BeGreaterThan(0);
            }
        );
    }

    [Test]
    public async Task CancelExecution_AtScale_WithinBudget()
    {
        await MeasureAsync(
            "operations.cancelExecution",
            ListBudget,
            async (sp, ct) =>
            {
                // The row may already be terminal (count 0); the point is the id-indexed
                // ExecuteUpdate stays fast against the huge metadata table.
                await new OperationsMutations().CancelExecution(
                    Profile.Metadata / 2,
                    Operations(sp),
                    ct
                );
            }
        );
    }

    [Test]
    public async Task CancelWorkQueueEntries_AtScale_WithinBudget()
    {
        await MeasureAsync(
            "operations.workQueue.cancelWorkQueueEntries",
            ListBudget,
            async (sp, ct) =>
            {
                await new WorkQueueMutations().CancelWorkQueueEntries(
                    [1, 2, 3, 4, 5],
                    Operations(sp),
                    ct
                );
            }
        );
    }

    #endregion

    #region Exact-count filters (a filter makes totalCount an exact COUNT over the matching rows)

    /// <summary>How many seeded runs satisfy <paramref name="match"/>, by the seed's arithmetic.</summary>
    private static int SeededRuns(Func<long, bool> match)
    {
        var count = 0;
        for (long g = 1; g <= Profile.Metadata; g++)
            if (match(g))
                count++;
        return count;
    }

    private static bool SeededFailed(long g) => g % 9 is 4 or 5;

    [Test]
    public async Task Executions_FilterByTrainState_CountsExactly_WithinBudget()
    {
        var failed = SeededRuns(SeededFailed);
        await MeasureAsync(
            "operations.executions (trainState: FAILED, exact count)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new OperationsQueries().GetExecutions(
                    Factory(sp),
                    ct,
                    take: 25,
                    trainState: TrainState.Failed,
                    sqlDialect: Dialect(sp)
                );
                page.Items.Should()
                    .HaveCount(25)
                    .And.OnlyContain(e => e.TrainState == TrainState.Failed);
                page.IsEstimatedCount.Should().BeFalse("a filtered total is exact");
                page.TotalCount.Should().Be(failed);
            }
        );
    }

    [Test]
    public async Task Executions_FilterByFailureClass_CountsExactly_WithinBudget()
    {
        var permanent = SeededRuns(g => SeededFailed(g) && g / 9 % 3 == 2);
        await MeasureAsync(
            "operations.executions (failureClass: PERMANENT, exact count)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new OperationsQueries().GetExecutions(
                    Factory(sp),
                    ct,
                    take: 25,
                    failureClass: FailureClass.Permanent,
                    sqlDialect: Dialect(sp)
                );
                page.Items.Should().HaveCount(25);
                page.IsEstimatedCount.Should().BeFalse("a filtered total is exact");
                page.TotalCount.Should().Be(permanent);
            }
        );
    }

    [Test]
    public async Task Executions_HideAdminTrains_CountsExactly_WithinBudget()
    {
        // The seed has no scheduler-internal runs, so the filter keeps every row and the exact
        // count is over the whole table: the worst case for it.
        await MeasureAsync(
            "operations.executions (hideAdminTrains, exact count)",
            ListBudget,
            async (sp, ct) =>
            {
                var page = await new OperationsQueries().GetExecutions(
                    Factory(sp),
                    ct,
                    take: 25,
                    hideAdminTrains: true,
                    sqlDialect: Dialect(sp)
                );
                page.Items.Should().HaveCount(25);
                page.IsEstimatedCount.Should().BeFalse("a filtered total is exact");
                page.TotalCount.Should().Be((int)Profile.Metadata);
            }
        );
    }

    #endregion
}
