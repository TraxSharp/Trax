using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Sqlite.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Sqlite.Integration.IntegrationTests;

/// <summary>
/// The dashboard metrics on Sqlite: the Overview and <c>operations.metrics.dashboard</c> read
/// them, so every aggregation, the average durations included, has to translate for this
/// provider and give the numbers the Postgres suite expects for the same seed.
/// </summary>
[TestFixture]
public class SqliteDashboardMetricsTests : TestSetup
{
    private IOperationsService _operations = null!;

    public override async Task TestSetUp()
    {
        await base.TestSetUp();
        _operations = Scope.ServiceProvider.GetRequiredService<IOperationsService>();
    }

    [TestCase(MetricsRange.Last24Hours, 24)]
    [TestCase(MetricsRange.Last60Minutes, 60)]
    public async Task Every_aggregation_reads_on_Sqlite(MetricsRange range, int buckets)
    {
        // Whole milliseconds: Sqlite reads three fractional digits, so a start with finer ticks would
        // shift a 250 ms duration by up to a millisecond and the average past its tolerance.
        var now = DateTime.UtcNow.AddMinutes(-30);
        var start = new DateTime(
            now.Ticks - now.Ticks % TimeSpan.TicksPerMillisecond,
            DateTimeKind.Utc
        );
        await Seed("Trax.Tests.ISlow", TrainState.Completed, start, start.AddSeconds(10));
        await Seed("Trax.Tests.ISlow", TrainState.Completed, start, start.AddSeconds(20));
        await Seed("Trax.Tests.IFast", TrainState.Completed, start, start.AddMilliseconds(250));
        await Seed("Trax.Tests.IFast", TrainState.Failed, start, start.AddSeconds(1));
        await Seed("Trax.Tests.IFast", TrainState.InProgress, start, null);
        // A child run is not a root, so it takes no part in the average durations.
        await Seed(
            "Trax.Tests.IChild",
            TrainState.Completed,
            start,
            start.AddMinutes(5),
            parentId: (await Seed("Trax.Tests.IParent", TrainState.Failed, start, start)).Id
        );

        var metrics = await _operations.GetDashboardMetricsAsync(
            range,
            hideAdminTrains: false,
            CancellationToken.None
        );

        metrics.Kpis.CurrentlyRunning.Should().Be(1);
        metrics.ExecutionsOverTime.Should().HaveCount(buckets);
        metrics.ExecutionsOverTime.Sum(b => b.Completed).Should().Be(4);
        metrics.ExecutionsOverTime.Sum(b => b.Failed).Should().Be(2);
        metrics
            .TopFailures.Should()
            .BeEquivalentTo(
                new[]
                {
                    new TrainFailureCount("Trax.Tests.IFast", 1),
                    new TrainFailureCount("Trax.Tests.IParent", 1),
                }
            );
        metrics
            .TopAverageDurations.Select(d => d.TrainName)
            .Should()
            .Equal("Trax.Tests.ISlow", "Trax.Tests.IFast");
        metrics.TopAverageDurations[0].AverageMilliseconds.Should().BeApproximately(15000, 1);
        metrics.TopAverageDurations[1].AverageMilliseconds.Should().BeApproximately(250, 1);
        metrics.ThroughputSeries.Should().NotBeEmpty();
    }

    [Test]
    public async Task Average_durations_leave_out_admin_trains_when_asked()
    {
        var start = DateTime.UtcNow.AddHours(-1);
        await Seed("Trax.Tests.IUser", TrainState.Completed, start, start.AddSeconds(2));
        await Seed(AdminTrains.FullNames[0], TrainState.Completed, start, start.AddSeconds(9));

        var metrics = await _operations.GetDashboardMetricsAsync(
            MetricsRange.Last24Hours,
            hideAdminTrains: true,
            CancellationToken.None
        );

        metrics.TopAverageDurations.Should().ContainSingle();
        metrics.TopAverageDurations[0].TrainName.Should().Be("Trax.Tests.IUser");
        metrics.TopAverageDurations[0].AverageMilliseconds.Should().BeApproximately(2000, 1);
    }

    [Test]
    public async Task A_trains_metric_series_reads_on_Sqlite()
    {
        var now = DateTime.UtcNow;
        var start = new DateTime(
            now.Ticks - now.Ticks % TimeSpan.TicksPerHour,
            DateTimeKind.Utc
        ).AddHours(-3);
        await Seed("Trax.Tests.ISeries", TrainState.Completed, start, start.AddSeconds(2));
        await Seed("Trax.Tests.ISeries", TrainState.Completed, start, start.AddSeconds(4));
        await Seed("Trax.Tests.ISeries", TrainState.Failed, start, start.AddSeconds(1));
        await Seed("Trax.Tests.IOther", TrainState.Completed, start, start.AddSeconds(9));

        var series = await _operations.GetMetricSeriesAsync(
            MetricSeriesScope.Train,
            null,
            "Trax.Tests.ISeries",
            MetricSeriesRange.Last7Days,
            CancellationToken.None
        );

        var bucket = series.Buckets.Single(b => b.Start == start);
        bucket.Completed.Should().Be(2);
        bucket.Failed.Should().Be(1);
        // Sqlite reads a date difference through julianday, a double, so a duration can come back a
        // few hundredths of a millisecond short.
        bucket.P50DurationMs.Should().BeInRange(1999, 4001);
        bucket.P95DurationMs.Should().BeInRange(1999, 4001);
        series.Buckets.Sum(b => b.Completed).Should().Be(2);
    }

    private async Task<Metadata> Seed(
        string name,
        TrainState state,
        DateTime start,
        DateTime? end,
        long? parentId = null
    )
    {
        var meta = Metadata.Create(
            new CreateMetadata
            {
                Name = name,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        meta.TrainState = state;
        meta.StartTime = DateTime.SpecifyKind(start, DateTimeKind.Utc);
        meta.EndTime = end is { } e ? DateTime.SpecifyKind(e, DateTimeKind.Utc) : null;
        meta.ParentId = parentId;
        await DataContext.Track(meta);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return meta;
    }
}
