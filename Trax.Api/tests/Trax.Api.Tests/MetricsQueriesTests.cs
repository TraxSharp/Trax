using AwesomeAssertions;
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
    public async Task GetSeries_ForATrainWithTenRunsOverTwoHours_HasTheRightCountsPerBucketAndAP95WithinTheRecordedDurations()
    {
        var factory =
            new Trax.Effect.Data.InMemory.Services.InMemoryContextFactory.InMemoryContextProviderFactory(
                new Microsoft.EntityFrameworkCore.Storage.InMemoryDatabaseRoot()
            );
        const string train = "Trax.Tests.ISeriesTrain";
        var now = DateTime.UtcNow;
        var quarter = TimeSpan.FromMinutes(15).Ticks;
        var t0 = new DateTime(now.Ticks - now.Ticks % quarter, DateTimeKind.Utc).AddHours(-2);
        var durations = new List<(DateTime Start, double Ms)>();
        await using (var db = await factory.CreateDbContextAsync(default))
        {
            // Ten runs, one every 12 minutes; the fourth failed, the rest completed in (i + 1) seconds.
            for (var i = 0; i < 10; i++)
            {
                var start = t0.AddMinutes(12 * i);
                var run = Trax.Effect.Models.Metadata.Metadata.Create(
                    new Trax.Effect.Models.Metadata.DTOs.CreateMetadata
                    {
                        Name = train,
                        ExternalId = Guid.NewGuid().ToString("N"),
                        Input = null,
                    }
                );
                run.TrainState =
                    i == 3
                        ? Trax.Effect.Enums.TrainState.Failed
                        : Trax.Effect.Enums.TrainState.Completed;
                run.StartTime = start;
                run.EndTime = start.AddSeconds(i + 1);
                await db.Track(run);
                if (i != 3)
                    durations.Add((start, (i + 1) * 1000));
            }
            await db.SaveChanges(default);
        }
        var ops = new OperationsService(
            Substitute.For<Trax.Mediator.Services.TrainDiscovery.ITrainDiscoveryService>(),
            factory,
            new Trax.Scheduler.Configuration.SchedulerConfiguration(),
            Substitute.For<Trax.Mediator.Services.TrainExecution.ITrainExecutionService>()
        );

        var series = await new MetricsQueries().GetSeries(
            ops,
            default,
            MetricSeriesScope.Train,
            MetricSeriesRange.Last24Hours,
            name: train
        );

        series.Buckets.Should().HaveCount(96);
        // Minutes 0,12 | 24 | 36 (failed) | 48 | 60,72 | 84 | 96 | 108 from t0, in 15-minute buckets.
        int[] completed = [2, 1, 0, 1, 2, 1, 1, 1];
        for (var b = 0; b < completed.Length; b++)
        {
            var bucketStart = t0.AddMinutes(15 * b);
            var bucket = series.Buckets.Single(x => x.Start == bucketStart);
            bucket.Completed.Should().Be(completed[b], $"bucket {b}");
            bucket.Failed.Should().Be(b == 2 ? 1 : 0, $"bucket {b}");
            var recorded = durations
                .Where(d => d.Start >= bucketStart && d.Start < bucketStart.AddMinutes(15))
                .Select(d => d.Ms)
                .ToList();
            if (recorded.Count == 0)
                bucket.P95DurationMs.Should().BeNull();
            else
                bucket.P95DurationMs.Should().BeInRange(recorded.Min(), recorded.Max());
        }
        series.Buckets.Sum(x => x.Completed + x.Failed).Should().Be(10);
    }

    [TestCase(MetricSeriesScope.Train, 7L, null)]
    [TestCase(MetricSeriesScope.Manifest, null, "Trax.Tests.ITrain")]
    [TestCase(MetricSeriesScope.Group, null, null)]
    public async Task GetSeries_WithoutTheKeyItsScopeNeeds_IsRefusedBeforeAnyRead(
        MetricSeriesScope scope,
        long? id,
        string? name
    ) => await RefusedBeforeAnyRead(scope, MetricSeriesRange.LastHour, id, name);

    [TestCase(MetricSeriesScope.Train, null, "Trax.Tests.ITrain")]
    [TestCase(MetricSeriesScope.Manifest, 7L, null)]
    [TestCase(MetricSeriesScope.Group, 7L, null)]
    public async Task GetSeries_OverThirtyDays_IsRefusedBeforeAnyRead(
        MetricSeriesScope scope,
        long? id,
        string? name
    ) => await RefusedBeforeAnyRead(scope, MetricSeriesRange.Last30Days, id, name);

    private static async Task RefusedBeforeAnyRead(
        MetricSeriesScope scope,
        MetricSeriesRange range,
        long? id,
        string? name
    )
    {
        var ops = Substitute.For<IOperationsService>();

        var read = () => new MetricsQueries().GetSeries(ops, default, scope, range, id, name);

        (await read.Should().ThrowAsync<HotChocolate.GraphQLException>())
            .Which.Errors.Single()
            .Code.Should()
            .Be(MetricsQueries.SeriesArgumentCode);
        await ops.DidNotReceiveWithAnyArgs()
            .GetMetricSeriesAsync(default, default, default, default, default);
    }

    [Test]
    public async Task GetSeries_ForwardsScopeIdAndRange()
    {
        var ops = Substitute.For<IOperationsService>();
        var result = new MetricSeries(
            MetricSeriesScope.Group,
            MetricSeriesRange.Last7Days,
            DateTime.UtcNow,
            DateTime.UtcNow,
            3600,
            []
        );
        ops.GetMetricSeriesAsync(
                MetricSeriesScope.Group,
                5,
                null,
                MetricSeriesRange.Last7Days,
                Arg.Any<CancellationToken>()
            )
            .Returns(result);

        var actual = await new MetricsQueries().GetSeries(
            ops,
            default,
            MetricSeriesScope.Group,
            MetricSeriesRange.Last7Days,
            id: 5
        );

        actual.Should().BeSameAs(result);
    }

    [Test]
    public void OperationsQueries_MetricsNamespace_ReturnsNewInstance()
    {
        new OperationsQueries().Metrics().Should().NotBeNull();
    }
}
