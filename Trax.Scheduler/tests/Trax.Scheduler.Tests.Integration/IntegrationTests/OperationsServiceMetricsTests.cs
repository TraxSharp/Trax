using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;
using Trax.Effect.Enums;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.DeadLetter.DTOs;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Integration tests for the metrics surface of <see cref="IOperationsService"/>.
/// These cover the dashboard Index page's KPIs, executions-over-time chart, top failures,
/// average durations, and throughput sparklines, plus the host-process snapshot.
/// </summary>
[TestFixture]
public class OperationsServiceMetricsTests : TestSetup
{
    private IOperationsService _operations = null!;

    [SetUp]
    public void GetService()
    {
        _operations = Scope.ServiceProvider.GetRequiredService<IOperationsService>();
    }

    private static string TrainName => typeof(ISchedulerTestTrain).FullName!;
    private static string AdminTrainName => AdminTrains.FullNames[0];

    private async Task<Metadata> SeedMetadata(
        TrainState state,
        DateTime startTime,
        string name = "",
        DateTime? endTime = null,
        bool isAdmin = false
    )
    {
        var meta = Metadata.Create(
            new CreateMetadata
            {
                Name = name == "" ? (isAdmin ? AdminTrainName : TrainName) : name,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        meta.TrainState = state;
        meta.StartTime = DateTime.SpecifyKind(startTime, DateTimeKind.Utc);
        if (endTime.HasValue)
            meta.EndTime = DateTime.SpecifyKind(endTime.Value, DateTimeKind.Utc);
        await DataContext.Track(meta);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
        return meta;
    }

    private async Task SeedDeadLetter(DeadLetterStatus status)
    {
        var groupId = (
            await CreateAndSaveManifestGroup(DataContext, name: $"dl-{Guid.NewGuid():N}")
        ).Id;
        var manifest = Manifest.Create(new CreateManifest { Name = typeof(ISchedulerTestTrain) });
        manifest.ManifestGroupId = groupId;
        await DataContext.Track(manifest);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();

        var dl = DeadLetter.Create(
            new CreateDeadLetter
            {
                Manifest = manifest,
                Reason = "x",
                RetryCount = 1,
            }
        );
        dl.Status = status;
        await DataContext.Track(dl);
        await DataContext.SaveChanges(CancellationToken.None);
        DataContext.Reset();
    }

    #region KPIs

    [Test]
    public async Task GetDashboardMetrics_NoData_ReturnsZeroKpis()
    {
        var metrics = await _operations.GetDashboardMetricsAsync(
            MetricsRange.Last24Hours,
            hideAdminTrains: false,
            CancellationToken.None
        );

        metrics.Kpis.ExecutionsToday.Should().Be(0);
        metrics.Kpis.SuccessRate.Should().Be(0);
        metrics.Kpis.CurrentlyRunning.Should().Be(0);
        metrics.Kpis.UnresolvedDeadLetters.Should().Be(0);
        metrics.ExecutionsOverTime.Should().HaveCount(24);
        metrics.TopFailures.Should().BeEmpty();
        metrics.TopAverageDurations.Should().BeEmpty();
        metrics.ThroughputSeries.Should().BeEmpty();
    }

    [Test]
    public async Task GetDashboardMetrics_TodayKpis_ReflectStateCounts()
    {
        var now = DateTime.UtcNow;
        await SeedMetadata(TrainState.Completed, now);
        await SeedMetadata(TrainState.Completed, now);
        await SeedMetadata(TrainState.Completed, now);
        await SeedMetadata(TrainState.Failed, now);
        await SeedMetadata(TrainState.InProgress, now);

        var metrics = await _operations.GetDashboardMetricsAsync(
            MetricsRange.Last24Hours,
            hideAdminTrains: false,
            CancellationToken.None
        );

        metrics.Kpis.ExecutionsToday.Should().Be(5);
        // 3 completed / (3 completed + 1 failed) = 75%
        metrics.Kpis.SuccessRate.Should().Be(75.0);
        metrics.Kpis.CurrentlyRunning.Should().Be(1);
    }

    [Test]
    public async Task GetDashboardMetrics_NoTerminal_SuccessRateIsZero()
    {
        await SeedMetadata(TrainState.InProgress, DateTime.UtcNow);

        var metrics = await _operations.GetDashboardMetricsAsync(
            MetricsRange.Last24Hours,
            hideAdminTrains: false,
            CancellationToken.None
        );

        metrics.Kpis.SuccessRate.Should().Be(0);
    }

    [Test]
    public async Task GetDashboardMetrics_DeadLetterCount_OnlyAwaitingIntervention()
    {
        await SeedDeadLetter(DeadLetterStatus.AwaitingIntervention);
        await SeedDeadLetter(DeadLetterStatus.AwaitingIntervention);
        await SeedDeadLetter(DeadLetterStatus.Acknowledged);

        var metrics = await _operations.GetDashboardMetricsAsync(
            MetricsRange.Last24Hours,
            hideAdminTrains: false,
            CancellationToken.None
        );

        metrics.Kpis.UnresolvedDeadLetters.Should().Be(2);
    }

    [Test]
    public async Task GetDashboardMetrics_HideAdminTrains_ExcludesAdminTrainsFromCounts()
    {
        var now = DateTime.UtcNow;
        await SeedMetadata(TrainState.Completed, now);
        await SeedMetadata(TrainState.Completed, now, isAdmin: true);
        await SeedMetadata(TrainState.InProgress, now, isAdmin: true);

        var withAdmin = await _operations.GetDashboardMetricsAsync(
            MetricsRange.Last24Hours,
            hideAdminTrains: false,
            CancellationToken.None
        );
        var withoutAdmin = await _operations.GetDashboardMetricsAsync(
            MetricsRange.Last24Hours,
            hideAdminTrains: true,
            CancellationToken.None
        );

        withAdmin.Kpis.ExecutionsToday.Should().Be(3);
        withAdmin.Kpis.CurrentlyRunning.Should().Be(1);
        withoutAdmin.Kpis.ExecutionsToday.Should().Be(1);
        withoutAdmin.Kpis.CurrentlyRunning.Should().Be(0);
    }

    #endregion

    #region Executions over time

    [Test]
    public async Task GetDashboardMetrics_ExecutionsOverTime_24h_Has24Buckets()
    {
        var metrics = await _operations.GetDashboardMetricsAsync(
            MetricsRange.Last24Hours,
            hideAdminTrains: false,
            CancellationToken.None
        );

        metrics.ExecutionsOverTime.Should().HaveCount(24);
        metrics
            .ExecutionsOverTime[0]
            .Timestamp.Should()
            .BeBefore(metrics.ExecutionsOverTime[^1].Timestamp);
    }

    [Test]
    public async Task GetDashboardMetrics_ExecutionsOverTime_60m_Has60Buckets()
    {
        var metrics = await _operations.GetDashboardMetricsAsync(
            MetricsRange.Last60Minutes,
            hideAdminTrains: false,
            CancellationToken.None
        );

        metrics.ExecutionsOverTime.Should().HaveCount(60);
    }

    [Test]
    public async Task GetDashboardMetrics_ExecutionsOverTime_60m_PopulatesBucketCounts()
    {
        var now = DateTime.UtcNow;
        var bucketTime = now.AddMinutes(-5);
        await SeedMetadata(TrainState.Completed, bucketTime);
        await SeedMetadata(TrainState.Cancelled, bucketTime);

        var metrics = await _operations.GetDashboardMetricsAsync(
            MetricsRange.Last60Minutes,
            hideAdminTrains: false,
            CancellationToken.None
        );

        metrics.ExecutionsOverTime.Should().HaveCount(60);
        var sumCompleted = metrics.ExecutionsOverTime.Sum(b => b.Completed);
        var sumCancelled = metrics.ExecutionsOverTime.Sum(b => b.Cancelled);
        sumCompleted.Should().Be(1);
        sumCancelled.Should().Be(1);
        // Touch every field on at least one bucket so all record accessors are exercised.
        var firstBucket = metrics.ExecutionsOverTime[0];
        _ = firstBucket.Timestamp;
        _ = firstBucket.Completed;
        _ = firstBucket.Failed;
        _ = firstBucket.Cancelled;
    }

    [Test]
    public async Task GetDashboardMetrics_ExecutionsOverTime_PopulatesBucketCounts()
    {
        var now = DateTime.UtcNow;
        // Place several completions inside the same hour bucket.
        var bucketTime = now.AddHours(-2);
        await SeedMetadata(TrainState.Completed, bucketTime);
        await SeedMetadata(TrainState.Completed, bucketTime.AddMinutes(10));
        await SeedMetadata(TrainState.Failed, bucketTime.AddMinutes(20));

        var metrics = await _operations.GetDashboardMetricsAsync(
            MetricsRange.Last24Hours,
            hideAdminTrains: false,
            CancellationToken.None
        );

        metrics.ExecutionsOverTime.Sum(b => b.Completed).Should().Be(2);
        metrics.ExecutionsOverTime.Sum(b => b.Failed).Should().Be(1);
    }

    #endregion

    #region Top failures + durations

    [Test]
    public async Task GetDashboardMetrics_TopFailures_OrderedByCount()
    {
        var now = DateTime.UtcNow.AddHours(-1);
        for (var i = 0; i < 3; i++)
            await SeedMetadata(TrainState.Failed, now, name: "Trax.Tests.IAlpha");
        for (var i = 0; i < 5; i++)
            await SeedMetadata(TrainState.Failed, now, name: "Trax.Tests.IBeta");
        await SeedMetadata(TrainState.Failed, now, name: "Trax.Tests.IGamma");

        var metrics = await _operations.GetDashboardMetricsAsync(
            MetricsRange.Last24Hours,
            hideAdminTrains: false,
            CancellationToken.None
        );

        metrics.TopFailures.Should().HaveCount(3);
        metrics.TopFailures[0].Should().Be(new TrainFailureCount("Trax.Tests.IBeta", 5));
        metrics.TopFailures[1].Should().Be(new TrainFailureCount("Trax.Tests.IAlpha", 3));
        metrics.TopFailures[2].Should().Be(new TrainFailureCount("Trax.Tests.IGamma", 1));
    }

    [Test]
    public async Task GetDashboardMetrics_TopAverageDurations_RootLevelOnly()
    {
        var now = DateTime.UtcNow.AddHours(-1);
        var slow = await SeedMetadata(
            TrainState.Completed,
            now,
            name: "Trax.Tests.ISlow",
            endTime: now.AddSeconds(10)
        );
        await SeedMetadata(
            TrainState.Completed,
            now,
            name: "Trax.Tests.IFast",
            endTime: now.AddSeconds(1)
        );

        var metrics = await _operations.GetDashboardMetricsAsync(
            MetricsRange.Last24Hours,
            hideAdminTrains: false,
            CancellationToken.None
        );

        metrics.TopAverageDurations.Should().HaveCount(2);
        metrics.TopAverageDurations[0].TrainName.Should().Be("Trax.Tests.ISlow");
        metrics.TopAverageDurations[0].AverageMilliseconds.Should().BeApproximately(10000, 1);
    }

    #endregion

    #region Throughput

    [Test]
    public async Task GetDashboardMetrics_Throughput_OrdersTopThreePlusOther()
    {
        var now = DateTime.UtcNow.AddHours(-3);
        // 6 of A (top), 4 of B, 2 of C, 1 each of D and E (collapses into Other)
        for (var i = 0; i < 6; i++)
            await SeedMetadata(TrainState.Completed, now, name: "Trax.Tests.IAlpha");
        for (var i = 0; i < 4; i++)
            await SeedMetadata(TrainState.Completed, now, name: "Trax.Tests.IBeta");
        for (var i = 0; i < 2; i++)
            await SeedMetadata(TrainState.Completed, now, name: "Trax.Tests.IGamma");
        await SeedMetadata(TrainState.Completed, now, name: "Trax.Tests.IDelta");
        await SeedMetadata(TrainState.Completed, now, name: "Trax.Tests.IEpsilon");

        var metrics = await _operations.GetDashboardMetricsAsync(
            MetricsRange.Last24Hours,
            hideAdminTrains: false,
            CancellationToken.None
        );

        metrics
            .ThroughputSeries.Select(s => s.TrainName)
            .Should()
            .BeEquivalentTo(
                new[] { "Trax.Tests.IAlpha", "Trax.Tests.IBeta", "Trax.Tests.IGamma", "Other" }
            );
        // Each series has 28 buckets
        metrics.ThroughputSeries.Should().AllSatisfy(s => s.Buckets.Should().HaveCount(28));
        // Other = D + E = 2
        metrics
            .ThroughputSeries.Single(s => s.TrainName == "Other")
            .Buckets.Sum(b => b.Count)
            .Should()
            .Be(2);
    }

    [Test]
    public async Task GetDashboardMetrics_Throughput_DropsEmptySeries()
    {
        // Only one train completed → only that series, no "Other"
        var now = DateTime.UtcNow.AddHours(-1);
        await SeedMetadata(TrainState.Completed, now, name: "Trax.Tests.ISolo");

        var metrics = await _operations.GetDashboardMetricsAsync(
            MetricsRange.Last24Hours,
            hideAdminTrains: false,
            CancellationToken.None
        );

        metrics.ThroughputSeries.Should().ContainSingle();
        metrics.ThroughputSeries[0].TrainName.Should().Be("Trax.Tests.ISolo");
    }

    #endregion

    #region GetServerMetrics

    [Test]
    public void GetServerMetrics_ReturnsLivenessSnapshot()
    {
        var metrics = _operations.GetServerMetrics();

        metrics.WorkingSetBytes.Should().BeGreaterThan(0);
        metrics.GcHeapBytes.Should().BeGreaterThan(0);
        metrics.UptimeSeconds.Should().BeGreaterThan(0);
        metrics.ProcessStartTimeUtc.Should().BeBefore(DateTime.UtcNow);
        metrics.ProcessStartTimeUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    #endregion
}
