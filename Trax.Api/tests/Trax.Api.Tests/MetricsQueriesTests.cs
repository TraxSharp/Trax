using FluentAssertions;
using NSubstitute;
using NUnit.Framework;
using Trax.Api.GraphQL.Queries;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.Tests;

/// <summary>
/// Pass-through tests for the GraphQL <c>operations.metrics</c> namespace. The
/// underlying behaviour lives in
/// <c>Trax.Scheduler.Tests.Integration.OperationsServiceMetricsTests</c>; here we just
/// verify the GraphQL layer forwards arguments and returns the service's result
/// unchanged.
/// </summary>
[TestFixture]
public class MetricsQueriesTests
{
    [Test]
    public async Task GetDashboard_ForwardsRangeAndHideAdmin()
    {
        var ops = Substitute.For<IOperationsService>();
        var result = new DashboardMetrics(
            new DashboardKpis(1, 2.5, 3, 4),
            Array.Empty<ExecutionsBucket>(),
            Array.Empty<TrainFailureCount>(),
            Array.Empty<TrainAverageDuration>(),
            Array.Empty<ThroughputSeries>()
        );
        ops.GetDashboardMetricsAsync(MetricsRange.Last60Minutes, true, Arg.Any<CancellationToken>())
            .Returns(result);
        var queries = new MetricsQueries();

        var actual = await queries.GetDashboard(
            ops,
            default,
            MetricsRange.Last60Minutes,
            hideAdminTrains: true
        );

        actual.Should().BeSameAs(result);
        await ops.Received(1)
            .GetDashboardMetricsAsync(
                MetricsRange.Last60Minutes,
                true,
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public async Task GetDashboard_DefaultsRangeAnd24hAndShowAdminTrains()
    {
        var ops = Substitute.For<IOperationsService>();
        ops.GetDashboardMetricsAsync(
                Arg.Any<MetricsRange>(),
                Arg.Any<bool>(),
                Arg.Any<CancellationToken>()
            )
            .Returns(
                new DashboardMetrics(
                    new DashboardKpis(0, 0, 0, 0),
                    Array.Empty<ExecutionsBucket>(),
                    Array.Empty<TrainFailureCount>(),
                    Array.Empty<TrainAverageDuration>(),
                    Array.Empty<ThroughputSeries>()
                )
            );
        var queries = new MetricsQueries();

        await queries.GetDashboard(ops, default);

        await ops.Received(1)
            .GetDashboardMetricsAsync(
                MetricsRange.Last24Hours,
                false,
                Arg.Any<CancellationToken>()
            );
    }

    [Test]
    public void GetServer_ForwardsToOperationsService()
    {
        var ops = Substitute.For<IOperationsService>();
        var snap = new ServerMetrics(
            DateTime.UtcNow.AddHours(-1),
            UptimeSeconds: 3600,
            WorkingSetBytes: 1024 * 1024,
            GcHeapBytes: 512 * 1024
        );
        ops.GetServerMetrics().Returns(snap);
        var queries = new MetricsQueries();

        queries.GetServer(ops).Should().BeSameAs(snap);
    }

    [Test]
    public void OperationsQueries_MetricsNamespace_ReturnsNewInstance()
    {
        new OperationsQueries().Metrics().Should().NotBeNull();
    }
}
