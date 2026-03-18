using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Scheduling;
using Trax.Scheduler.Trains.ManifestManager.Utilities;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

[TestFixture]
public class ScheduleVarianceTests
{
    private SchedulerConfiguration _config = null!;
    private ILogger _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _config = new SchedulerConfiguration();
        _logger = NullLoggerFactory.Instance.CreateLogger("test");
    }

    #region ComputeNextScheduledRun

    [Test]
    public void ComputeNextScheduledRun_IntervalWithVariance_ReturnsTimeInExpectedRange()
    {
        var lastRun = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);
        var manifest = new Manifest
        {
            ScheduleType = ScheduleType.Interval,
            IntervalSeconds = 300, // 5 minutes
            VarianceSeconds = 120, // 2 minutes
            LastSuccessfulRun = lastRun,
        };

        var baseNext = lastRun.AddSeconds(300);
        var maxNext = baseNext.AddSeconds(120);

        // Run multiple times to exercise randomness
        for (var i = 0; i < 50; i++)
        {
            var result = SchedulingHelpers.ComputeNextScheduledRun(manifest);
            result.Should().NotBeNull();
            result!.Value.Should().BeOnOrAfter(baseNext);
            result.Value.Should().BeOnOrBefore(maxNext);
        }
    }

    [Test]
    public void ComputeNextScheduledRun_CronWithVariance_ReturnsTimeAfterNextOccurrence()
    {
        var lastRun = new DateTime(2026, 3, 10, 2, 0, 0, DateTimeKind.Utc);
        var manifest = new Manifest
        {
            ScheduleType = ScheduleType.Cron,
            CronExpression = "0 3 * * *", // daily at 3am
            VarianceSeconds = 1800, // 30 minutes
            LastSuccessfulRun = lastRun,
        };

        var baseCronNext = new DateTime(2026, 3, 10, 3, 0, 0, DateTimeKind.Utc);
        var maxNext = baseCronNext.AddSeconds(1800);

        for (var i = 0; i < 50; i++)
        {
            var result = SchedulingHelpers.ComputeNextScheduledRun(manifest);
            result.Should().NotBeNull();
            result!.Value.Should().BeOnOrAfter(baseCronNext);
            result.Value.Should().BeOnOrBefore(maxNext);
        }
    }

    [Test]
    public void ComputeNextScheduledRun_NoVariance_ReturnsNull()
    {
        var manifest = new Manifest
        {
            ScheduleType = ScheduleType.Interval,
            IntervalSeconds = 300,
            VarianceSeconds = null,
            LastSuccessfulRun = DateTime.UtcNow,
        };

        SchedulingHelpers.ComputeNextScheduledRun(manifest).Should().BeNull();
    }

    [Test]
    public void ComputeNextScheduledRun_ZeroVariance_ReturnsNull()
    {
        var manifest = new Manifest
        {
            ScheduleType = ScheduleType.Interval,
            IntervalSeconds = 300,
            VarianceSeconds = 0,
            LastSuccessfulRun = DateTime.UtcNow,
        };

        SchedulingHelpers.ComputeNextScheduledRun(manifest).Should().BeNull();
    }

    [Test]
    public void ComputeNextScheduledRun_DependentScheduleType_ReturnsNull()
    {
        var manifest = new Manifest
        {
            ScheduleType = ScheduleType.Dependent,
            VarianceSeconds = 120,
            LastSuccessfulRun = DateTime.UtcNow,
        };

        SchedulingHelpers.ComputeNextScheduledRun(manifest).Should().BeNull();
    }

    [Test]
    public void ComputeNextScheduledRun_FirstRun_ReturnsNull()
    {
        var manifest = new Manifest
        {
            ScheduleType = ScheduleType.Interval,
            IntervalSeconds = 300,
            VarianceSeconds = 120,
            LastSuccessfulRun = null,
        };

        SchedulingHelpers.ComputeNextScheduledRun(manifest).Should().BeNull();
    }

    [Test]
    public void ComputeNextScheduledRun_VarianceGreaterThanInterval_StillWorks()
    {
        var lastRun = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);
        var manifest = new Manifest
        {
            ScheduleType = ScheduleType.Interval,
            IntervalSeconds = 60, // 1 minute
            VarianceSeconds = 300, // 5 minutes (larger than interval)
            LastSuccessfulRun = lastRun,
        };

        var baseNext = lastRun.AddSeconds(60);
        var maxNext = baseNext.AddSeconds(300);

        var result = SchedulingHelpers.ComputeNextScheduledRun(manifest);
        result.Should().NotBeNull();
        result!.Value.Should().BeOnOrAfter(baseNext);
        result.Value.Should().BeOnOrBefore(maxNext);
    }

    [Test]
    public void ComputeNextScheduledRun_OnceScheduleType_ReturnsNull()
    {
        var manifest = new Manifest
        {
            ScheduleType = ScheduleType.Once,
            VarianceSeconds = 120,
            LastSuccessfulRun = DateTime.UtcNow,
        };

        SchedulingHelpers.ComputeNextScheduledRun(manifest).Should().BeNull();
    }

    #endregion

    #region ShouldRunNow with NextScheduledRun

    [Test]
    public void ShouldRunNow_WithNextScheduledRunInFuture_ReturnsFalse()
    {
        var now = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);
        var manifest = new Manifest
        {
            ScheduleType = ScheduleType.Interval,
            IntervalSeconds = 300,
            VarianceSeconds = 120,
            LastSuccessfulRun = now.AddMinutes(-3),
            NextScheduledRun = now.AddMinutes(3), // in the future
        };

        SchedulingHelpers.ShouldRunNow(manifest, now, _config, _logger).Should().BeFalse();
    }

    [Test]
    public void ShouldRunNow_WithNextScheduledRunInPast_ReturnsTrue()
    {
        var now = new DateTime(2026, 3, 10, 12, 5, 0, DateTimeKind.Utc);
        var manifest = new Manifest
        {
            ScheduleType = ScheduleType.Interval,
            IntervalSeconds = 300,
            VarianceSeconds = 120,
            LastSuccessfulRun = now.AddMinutes(-6),
            NextScheduledRun = now.AddMinutes(-1), // in the past
        };

        SchedulingHelpers.ShouldRunNow(manifest, now, _config, _logger).Should().BeTrue();
    }

    [Test]
    public void ShouldRunNow_WithoutVariance_FallsBackToIntervalCalculation()
    {
        var now = new DateTime(2026, 3, 10, 12, 6, 0, DateTimeKind.Utc);
        var manifest = new Manifest
        {
            ScheduleType = ScheduleType.Interval,
            IntervalSeconds = 300,
            VarianceSeconds = null,
            LastSuccessfulRun = now.AddMinutes(-6),
            NextScheduledRun = null,
        };

        // 6 min since last run, interval is 5 min -> should run
        SchedulingHelpers.ShouldRunNow(manifest, now, _config, _logger).Should().BeTrue();
    }

    [Test]
    public void ShouldRunNow_CronWithNextScheduledRunInFuture_ReturnsFalse()
    {
        var now = new DateTime(2026, 3, 10, 2, 30, 0, DateTimeKind.Utc);
        var manifest = new Manifest
        {
            ScheduleType = ScheduleType.Cron,
            CronExpression = "0 3 * * *",
            VarianceSeconds = 1800,
            LastSuccessfulRun = now.AddHours(-23),
            NextScheduledRun = now.AddMinutes(60), // still in the future (3:30am)
        };

        SchedulingHelpers.ShouldRunNow(manifest, now, _config, _logger).Should().BeFalse();
    }

    [Test]
    public void ShouldRunNow_CronWithNextScheduledRunInPast_ReturnsTrue()
    {
        var now = new DateTime(2026, 3, 10, 3, 20, 0, DateTimeKind.Utc);
        var manifest = new Manifest
        {
            ScheduleType = ScheduleType.Cron,
            CronExpression = "0 3 * * *",
            VarianceSeconds = 900,
            LastSuccessfulRun = new DateTime(2026, 3, 9, 3, 5, 0, DateTimeKind.Utc),
            NextScheduledRun = new DateTime(2026, 3, 10, 3, 10, 0, DateTimeKind.Utc), // 3:10am, now is 3:20am
        };

        SchedulingHelpers.ShouldRunNow(manifest, now, _config, _logger).Should().BeTrue();
    }

    [Test]
    public void ShouldRunNow_WithNextScheduledRunAndExclusion_ExclusionWins()
    {
        var now = new DateTime(2026, 3, 10, 14, 0, 0, DateTimeKind.Utc); // 2pm Tuesday
        var manifest = new Manifest
        {
            ScheduleType = ScheduleType.Interval,
            IntervalSeconds = 300,
            VarianceSeconds = 120,
            LastSuccessfulRun = now.AddMinutes(-10),
            NextScheduledRun = now.AddMinutes(-5), // should be due
        };

        // Exclude all of Tuesday
        var exclusion = Exclude.DaysOfWeek(DayOfWeek.Tuesday);
        manifest.SetExclusions([exclusion]);

        SchedulingHelpers.ShouldRunNow(manifest, now, _config, _logger).Should().BeFalse();
    }

    [Test]
    public void ShouldRunNow_WithNextScheduledRunAndDoNothingMisfire_BeyondThreshold_SkipsRun()
    {
        // Set up timing so "now" is NOT near an interval boundary.
        // LastSuccessfulRun = 12:00, interval = 5min, so boundaries are 12:05, 12:10, 12:15, 12:20...
        // now = 12:22:30 — 2.5 min past the 12:20 boundary, beyond the 30s threshold.
        var lastRun = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 3, 10, 12, 22, 30, DateTimeKind.Utc);
        var manifest = new Manifest
        {
            ScheduleType = ScheduleType.Interval,
            IntervalSeconds = 300, // 5 min
            VarianceSeconds = 60,
            MisfirePolicy = MisfirePolicy.DoNothing,
            MisfireThresholdSeconds = 30, // 30 sec threshold
            LastSuccessfulRun = lastRun,
            NextScheduledRun = lastRun.AddMinutes(6), // 12:06 — long overdue
        };

        // DoNothing: most recent boundary is 12:20, sinceBoundary = 150s > 30s → skip
        SchedulingHelpers.ShouldRunNow(manifest, now, _config, _logger).Should().BeFalse();
    }

    [Test]
    public void ShouldRunNow_WithNextScheduledRunAndFireOnceNow_ReturnsTrue()
    {
        var now = new DateTime(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);
        var manifest = new Manifest
        {
            ScheduleType = ScheduleType.Interval,
            IntervalSeconds = 300,
            VarianceSeconds = 60,
            MisfirePolicy = MisfirePolicy.FireOnceNow,
            MisfireThresholdSeconds = 30,
            LastSuccessfulRun = now.AddMinutes(-20),
            NextScheduledRun = now.AddMinutes(-15), // overdue way past threshold, but FireOnceNow
        };

        SchedulingHelpers.ShouldRunNow(manifest, now, _config, _logger).Should().BeTrue();
    }

    #endregion

    #region Schedule.WithVariance fluent API

    [Test]
    public void WithVariance_FluentApi_SetsPropertyOnSchedule()
    {
        var schedule = Every.Minutes(5).WithVariance(TimeSpan.FromMinutes(2));

        schedule.Type.Should().Be(ScheduleType.Interval);
        schedule.Interval.Should().Be(TimeSpan.FromMinutes(5));
        schedule.Variance.Should().Be(TimeSpan.FromMinutes(2));
    }

    [Test]
    public void WithVariance_OnCronSchedule_SetsPropertyOnSchedule()
    {
        var schedule = Cron.Daily(3).WithVariance(TimeSpan.FromMinutes(30));

        schedule.Type.Should().Be(ScheduleType.Cron);
        schedule.CronExpression.Should().Be("0 3 * * *");
        schedule.Variance.Should().Be(TimeSpan.FromMinutes(30));
    }

    [Test]
    public void ScheduleOptions_Variance_FlowsToManifestOptions()
    {
        var opts = new ScheduleOptions();
        opts.Variance(TimeSpan.FromMinutes(5));

        var manifestOptions = opts.ToManifestOptions();
        manifestOptions.Variance.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Test]
    public void ScheduleOptions_NoVariance_ManifestOptionsVarianceIsNull()
    {
        var opts = new ScheduleOptions();

        var manifestOptions = opts.ToManifestOptions();
        manifestOptions.Variance.Should().BeNull();
    }

    #endregion
}
