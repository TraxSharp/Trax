using FluentAssertions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.SchedulerLiveness;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// Deterministic tests for the scheduler liveness monitor and health check. Time is driven by
/// a controllable <see cref="TimeProvider"/> so the staleness threshold can be exercised
/// without any wall-clock waits.
/// </summary>
[TestFixture]
public class SchedulerLivenessTests
{
    private static readonly DateTimeOffset Start = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private sealed class TestTimeProvider(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private static Task<HealthCheckResult> Check(
        SchedulerLivenessHealthCheck check,
        HealthStatus failureStatus = HealthStatus.Unhealthy
    )
    {
        var registration = new HealthCheckRegistration(
            "scheduler-liveness",
            _ => throw new NotImplementedException("factory is not invoked in this test"),
            failureStatus,
            tags: null
        );
        var context = new HealthCheckContext { Registration = registration };
        return check.CheckHealthAsync(context, CancellationToken.None);
    }

    #region Monitor

    [Test]
    public void Monitor_BeforeFirstCycle_LastDispatchIsNull()
    {
        var time = new TestTimeProvider(Start);
        var monitor = new SchedulerLivenessMonitor(time);

        monitor.LastDispatchCompletedAt.Should().BeNull();
        monitor.StartedAt.Should().Be(Start);
    }

    [Test]
    public void Monitor_RecordDispatchCycle_SetsLastDispatchToNow()
    {
        var time = new TestTimeProvider(Start);
        var monitor = new SchedulerLivenessMonitor(time);

        time.Advance(TimeSpan.FromSeconds(4));
        monitor.RecordDispatchCycle();

        monitor.LastDispatchCompletedAt.Should().Be(Start.AddSeconds(4));
    }

    #endregion

    #region HealthCheck

    [Test]
    public async Task HealthCheck_BeforeFirstCycle_WithinGrace_ReportsHealthy()
    {
        var time = new TestTimeProvider(Start);
        var monitor = new SchedulerLivenessMonitor(time);
        var config = new SchedulerConfiguration(); // default threshold floor is 30s
        var check = new SchedulerLivenessHealthCheck(monitor, config, time);

        // No cycle yet, but only a few seconds since startup -> still within the grace window.
        time.Advance(TimeSpan.FromSeconds(5));

        var result = await Check(check);
        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Test]
    public async Task HealthCheck_NeverDispatched_PastThreshold_ReportsUnhealthy()
    {
        var time = new TestTimeProvider(Start);
        var monitor = new SchedulerLivenessMonitor(time);
        var config = new SchedulerConfiguration();
        var check = new SchedulerLivenessHealthCheck(monitor, config, time);

        // The scheduler has been up well past the threshold without ever dispatching.
        time.Advance(TimeSpan.FromSeconds(31));

        var result = await Check(check);
        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Test]
    public async Task HealthCheck_AfterCycle_WithinThreshold_ReportsHealthy()
    {
        var time = new TestTimeProvider(Start);
        var monitor = new SchedulerLivenessMonitor(time);
        var config = new SchedulerConfiguration();
        var check = new SchedulerLivenessHealthCheck(monitor, config, time);

        monitor.RecordDispatchCycle();
        time.Advance(TimeSpan.FromSeconds(10));

        var result = await Check(check);
        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Test]
    public async Task HealthCheck_AfterCycle_PastThreshold_ReportsUnhealthy()
    {
        var time = new TestTimeProvider(Start);
        var monitor = new SchedulerLivenessMonitor(time);
        var config = new SchedulerConfiguration();
        var check = new SchedulerLivenessHealthCheck(monitor, config, time);

        monitor.RecordDispatchCycle();
        time.Advance(TimeSpan.FromSeconds(31));

        var result = await Check(check);
        result.Status.Should().Be(HealthStatus.Unhealthy);
    }

    [Test]
    public async Task HealthCheck_ThresholdOverride_TakesPrecedenceOverConfig()
    {
        var time = new TestTimeProvider(Start);
        var monitor = new SchedulerLivenessMonitor(time);
        var config = new SchedulerConfiguration
        {
            SchedulerLivenessThreshold = TimeSpan.FromMinutes(5),
        };
        var check = new SchedulerLivenessHealthCheck(monitor, config, time)
        {
            ThresholdOverride = TimeSpan.FromSeconds(5),
        };

        monitor.RecordDispatchCycle();
        time.Advance(TimeSpan.FromSeconds(10));

        var result = await Check(check);
        result
            .Status.Should()
            .Be(HealthStatus.Unhealthy, "the override is 5s even though config is 5m");
    }

    [Test]
    public async Task HealthCheck_UsesConfiguredThreshold()
    {
        var time = new TestTimeProvider(Start);
        var monitor = new SchedulerLivenessMonitor(time);
        var config = new SchedulerConfiguration
        {
            SchedulerLivenessThreshold = TimeSpan.FromSeconds(100),
        };
        var check = new SchedulerLivenessHealthCheck(monitor, config, time);

        monitor.RecordDispatchCycle();
        time.Advance(TimeSpan.FromSeconds(50));

        var result = await Check(check);
        result.Status.Should().Be(HealthStatus.Healthy);
    }

    [Test]
    public async Task HealthCheck_StaleWithDegradedFailureStatus_ReportsDegraded()
    {
        var time = new TestTimeProvider(Start);
        var monitor = new SchedulerLivenessMonitor(time);
        var config = new SchedulerConfiguration();
        var check = new SchedulerLivenessHealthCheck(monitor, config, time);

        monitor.RecordDispatchCycle();
        time.Advance(TimeSpan.FromSeconds(31));

        var result = await Check(check, failureStatus: HealthStatus.Degraded);
        result.Status.Should().Be(HealthStatus.Degraded);
    }

    #endregion

    #region DefaultThreshold

    [Test]
    public void DefaultThreshold_ScalesWithPollingInterval_AboveFloor()
    {
        var config = new SchedulerConfiguration
        {
            JobDispatcherPollingInterval = TimeSpan.FromSeconds(5),
        };

        SchedulerLivenessHealthCheck
            .DefaultThreshold(config)
            .Should()
            .Be(TimeSpan.FromSeconds(50), "ten cycles of a 5s interval");
    }

    [Test]
    public void DefaultThreshold_FastInterval_UsesThirtySecondFloor()
    {
        var config = new SchedulerConfiguration
        {
            JobDispatcherPollingInterval = TimeSpan.FromSeconds(1),
        };

        SchedulerLivenessHealthCheck
            .DefaultThreshold(config)
            .Should()
            .Be(TimeSpan.FromSeconds(30), "ten cycles of a 1s interval is below the floor");
    }

    #endregion
}
