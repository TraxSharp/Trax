using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Api.DTOs;
using Trax.Api.GraphQL.Mutations;
using Trax.Api.GraphQL.Queries;
using Trax.Api.Services.HealthCheck;
using Trax.Api.Tests.Stress.Fixtures;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Services.ChangeSignal;
using Trax.Mediator.Services.TrainDiscovery;
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
public class AdminEndpointStressTests : StressTestSetup
{
    private static IDataContextProviderFactory Factory(IServiceProvider sp) =>
        sp.GetRequiredService<IDataContextProviderFactory>();

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
                var page = await new OperationsQueries().GetExecutions(Factory(sp), ct, take: 25);
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
                    afterId: 200
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
    public async Task Executions_DeepOffset_IsSlowerThanKeyset_ProvingKeysetRequirement()
    {
        // Keyset at the far end stays within budget.
        var keyset = await TimeAsync(
            async (sp, ct) =>
                await new OperationsQueries().GetExecutions(Factory(sp), ct, take: 25, afterId: 200)
        );

        // OFFSET near the end of the table must scan every skipped row.
        var deepSkip = (int)Math.Max(0, Profile.Metadata - 50);
        var offset = await TimeAsync(
            async (sp, ct) =>
                await new OperationsQueries().GetExecutions(
                    Factory(sp),
                    ct,
                    skip: deepSkip,
                    take: 25
                )
        );

        TestContext.Out.WriteLine(
            $"keyset(far end)={keyset.TotalMilliseconds:F0}ms vs OFFSET({deepSkip:N0})="
                + $"{offset.TotalMilliseconds:F0}ms — ratio {offset.TotalMilliseconds / Math.Max(1, keyset.TotalMilliseconds):F1}x"
        );

        keyset
            .Should()
            .BeLessThan(
                ListBudget,
                "keyset pagination is the dashboard's required paging strategy"
            );
        offset
            .Should()
            .BeGreaterThan(
                keyset,
                "deep OFFSET scans all skipped rows; the client must page by afterId, never skip"
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
                var page = await new WorkQueueQueries().GetWorkQueues(Factory(sp), ct, take: 25);
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
                    status: WorkQueueStatus.Dispatched
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
                    trainName: "Trax.Stress.Trains.IStressTrain7"
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
                var page = await new LogQueries().GetLogs(Factory(sp), ct, take: 25);
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
                var page = await new LogQueries().GetLogs(Factory(sp), ct, take: 25, metadataId: 1);
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
                    Factory(sp),
                    ct,
                    take: 25,
                    minimumLevel: LogLevel.Error
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
                    Factory(sp),
                    ct,
                    take: 25,
                    category: "Trax.Stress.Category3"
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
                var page = await new OperationsQueries().GetManifests(Factory(sp), ct, take: 25);
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
                var page = await new ManifestGroupQueries().GetGroups(Factory(sp), ct, take: 25);
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
                    manifestId: 1
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
                    manifestGroupId: 1
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
                    manifestGroupId: 1
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
                var stats = await new OperationsQueries().GetManifestStats(1, Factory(sp), ct);
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
                var stats = await new ManifestGroupQueries().GetStats(groupIds, Factory(sp), ct);
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
                    startedAfter: DateTime.UtcNow.AddHours(-24)
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
                    order: SortOrder.Oldest
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
                    Factory(sp),
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
                    Factory(sp),
                    sp.GetRequiredService<ITraxChangeSignal>(),
                    ct
                );
            }
        );
    }

    #endregion
}
