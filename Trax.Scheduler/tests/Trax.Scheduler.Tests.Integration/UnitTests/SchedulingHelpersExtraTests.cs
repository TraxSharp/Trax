using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Trains.ManifestManager.Utilities;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// Coverage for SchedulingHelpers code paths the cron-focused suite doesn't reach:
/// the OnDemand/Dependent default arms, malformed cron handling, the IsTimeForCron /
/// IsTimeForInterval public helpers, and ComputeNextScheduledRun across schedule types.
/// </summary>
[TestFixture]
public class SchedulingHelpersExtraTests
{
    private static SchedulerConfiguration NewConfig() => new();

    private static Manifest NewManifest()
    {
        var m = Manifest.Create(new CreateManifest { Name = typeof(SomeTrain) });
        m.IsEnabled = true;
        return m;
    }

    private class SomeTrain { }

    [Test]
    public void ShouldRunNow_OnDemandSchedule_ReturnsFalse()
    {
        var m = NewManifest();
        m.ScheduleType = ScheduleType.OnDemand;

        SchedulingHelpers
            .ShouldRunNow(m, DateTime.UtcNow, NewConfig(), NullLogger.Instance)
            .Should()
            .BeFalse();
    }

    [Test]
    public void ShouldRunNow_DependentSchedule_ReturnsFalse()
    {
        var m = NewManifest();
        m.ScheduleType = ScheduleType.Dependent;

        SchedulingHelpers
            .ShouldRunNow(m, DateTime.UtcNow, NewConfig(), NullLogger.Instance)
            .Should()
            .BeFalse();
    }

    [Test]
    public void ShouldRunNow_CronWithMissingExpression_ReturnsFalse()
    {
        var m = NewManifest();
        m.ScheduleType = ScheduleType.Cron;
        m.CronExpression = null;

        SchedulingHelpers
            .ShouldRunNow(m, DateTime.UtcNow, NewConfig(), NullLogger.Instance)
            .Should()
            .BeFalse();
    }

    [Test]
    public void ShouldRunNow_CronWithMalformedExpression_DoesNotThrow()
    {
        var m = NewManifest();
        m.ScheduleType = ScheduleType.Cron;
        m.CronExpression = "totally not cron syntax @@@";

        Action act = () =>
            SchedulingHelpers.ShouldRunNow(m, DateTime.UtcNow, NewConfig(), NullLogger.Instance);

        act.Should().NotThrow();
    }

    [Test]
    public void ShouldRunNow_IntervalWithMissingSeconds_ReturnsFalse()
    {
        var m = NewManifest();
        m.ScheduleType = ScheduleType.Interval;
        m.IntervalSeconds = null;

        SchedulingHelpers
            .ShouldRunNow(m, DateTime.UtcNow, NewConfig(), NullLogger.Instance)
            .Should()
            .BeFalse();
    }

    [Test]
    public void ShouldRunNow_IntervalWithZeroSeconds_ReturnsFalse()
    {
        var m = NewManifest();
        m.ScheduleType = ScheduleType.Interval;
        m.IntervalSeconds = 0;

        SchedulingHelpers
            .ShouldRunNow(m, DateTime.UtcNow, NewConfig(), NullLogger.Instance)
            .Should()
            .BeFalse();
    }

    [Test]
    public void ShouldRunNow_OnceAlreadyRan_ReturnsFalse()
    {
        var m = NewManifest();
        m.ScheduleType = ScheduleType.Once;
        m.ScheduledAt = DateTime.UtcNow.AddMinutes(-1);
        m.LastSuccessfulRun = DateTime.UtcNow.AddMinutes(-1);

        SchedulingHelpers
            .ShouldRunNow(m, DateTime.UtcNow, NewConfig(), NullLogger.Instance)
            .Should()
            .BeFalse();
    }

    [Test]
    public void ShouldRunNow_CronOverdueWithinThreshold_Fires()
    {
        var m = NewManifest();
        m.ScheduleType = ScheduleType.Cron;
        m.CronExpression = "0 * * * *";
        m.LastSuccessfulRun = DateTime.UtcNow.AddHours(-2);
        m.MisfireThresholdSeconds = 7200;

        SchedulingHelpers
            .ShouldRunNow(m, DateTime.UtcNow, NewConfig(), NullLogger.Instance)
            .Should()
            .BeTrue();
    }

    [Test]
    public void ShouldRunNow_CronOverdueBeyondThreshold_FireOnceNow_StillFires()
    {
        var m = NewManifest();
        m.ScheduleType = ScheduleType.Cron;
        m.CronExpression = "0 * * * *";
        m.LastSuccessfulRun = DateTime.UtcNow.AddDays(-7);
        m.MisfirePolicy = MisfirePolicy.FireOnceNow;
        m.MisfireThresholdSeconds = 1;

        SchedulingHelpers
            .ShouldRunNow(m, DateTime.UtcNow, NewConfig(), NullLogger.Instance)
            .Should()
            .BeTrue();
    }

    [Test]
    public void ShouldRunNow_CronDoNothingPolicy_ExercisesEvaluateBoundary()
    {
        // The DoNothing policy with overdue cron triggers EvaluateCronBoundary, the path
        // unit-tested here. Outcome depends on wall-clock proximity to a cron tick — what
        // we assert is that the call completes without throwing.
        var m = NewManifest();
        m.ScheduleType = ScheduleType.Cron;
        m.CronExpression = "*/30 * * * *";
        m.LastSuccessfulRun = DateTime.UtcNow.AddDays(-2);
        m.MisfirePolicy = MisfirePolicy.DoNothing;
        m.MisfireThresholdSeconds = 1;

        Action act = () =>
            SchedulingHelpers.ShouldRunNow(m, DateTime.UtcNow, NewConfig(), NullLogger.Instance);

        act.Should().NotThrow();
    }

    [Test]
    public void ShouldRunNow_IntervalDoNothingPolicy_ExercisesEvaluateBoundary()
    {
        var m = NewManifest();
        m.ScheduleType = ScheduleType.Interval;
        m.IntervalSeconds = 60;
        m.LastSuccessfulRun = DateTime.UtcNow.AddHours(-3);
        m.MisfirePolicy = MisfirePolicy.DoNothing;
        m.MisfireThresholdSeconds = 1;

        Action act = () =>
            SchedulingHelpers.ShouldRunNow(m, DateTime.UtcNow, NewConfig(), NullLogger.Instance);

        act.Should().NotThrow();
    }

    [Test]
    public void ShouldRunNow_IntervalOverdueFireOnceNow_StillFires()
    {
        var m = NewManifest();
        m.ScheduleType = ScheduleType.Interval;
        m.IntervalSeconds = 60;
        m.LastSuccessfulRun = DateTime.UtcNow.AddDays(-1);
        m.MisfirePolicy = MisfirePolicy.FireOnceNow;
        m.MisfireThresholdSeconds = 1;

        SchedulingHelpers
            .ShouldRunNow(m, DateTime.UtcNow, NewConfig(), NullLogger.Instance)
            .Should()
            .BeTrue();
    }

    #region IsTimeForCron

    [Test]
    public void IsTimeForCron_NeverRun_ReturnsTrue()
    {
        SchedulingHelpers.IsTimeForCron(null, "* * * * *", DateTime.UtcNow).Should().BeTrue();
    }

    [Test]
    public void IsTimeForCron_RanRecently_ReturnsFalse()
    {
        SchedulingHelpers
            .IsTimeForCron(DateTime.UtcNow.AddSeconds(-1), "0 0 * * *", DateTime.UtcNow)
            .Should()
            .BeFalse();
    }

    [Test]
    public void IsTimeForCron_OverdueByLong_ReturnsTrue()
    {
        SchedulingHelpers
            .IsTimeForCron(DateTime.UtcNow.AddDays(-2), "0 0 * * *", DateTime.UtcNow)
            .Should()
            .BeTrue();
    }

    #endregion

    #region IsTimeForInterval

    [Test]
    public void IsTimeForInterval_NeverRun_ReturnsTrue()
    {
        SchedulingHelpers.IsTimeForInterval(null, 60, DateTime.UtcNow).Should().BeTrue();
    }

    [Test]
    public void IsTimeForInterval_NotYetDue_ReturnsFalse()
    {
        SchedulingHelpers
            .IsTimeForInterval(DateTime.UtcNow.AddSeconds(-30), 60, DateTime.UtcNow)
            .Should()
            .BeFalse();
    }

    [Test]
    public void IsTimeForInterval_PastDue_ReturnsTrue()
    {
        SchedulingHelpers
            .IsTimeForInterval(DateTime.UtcNow.AddMinutes(-5), 60, DateTime.UtcNow)
            .Should()
            .BeTrue();
    }

    #endregion

    #region ComputeNextScheduledRun (via reflection — internal)

    [Test]
    public void ComputeNextScheduledRun_NoVariance_ReturnsNull()
    {
        var m = NewManifest();
        m.ScheduleType = ScheduleType.Interval;
        m.IntervalSeconds = 60;
        m.VarianceSeconds = null;
        m.LastSuccessfulRun = DateTime.UtcNow;

        var result = Invoke(m);
        result.Should().BeNull();
    }

    [Test]
    public void ComputeNextScheduledRun_NoLastRun_ReturnsNull()
    {
        var m = NewManifest();
        m.ScheduleType = ScheduleType.Interval;
        m.IntervalSeconds = 60;
        m.VarianceSeconds = 10;
        m.LastSuccessfulRun = null;

        var result = Invoke(m);
        result.Should().BeNull();
    }

    [Test]
    public void ComputeNextScheduledRun_Interval_AppliesVariance()
    {
        var m = NewManifest();
        m.ScheduleType = ScheduleType.Interval;
        m.IntervalSeconds = 60;
        m.VarianceSeconds = 10;
        m.LastSuccessfulRun = DateTime.UtcNow;

        var result = Invoke(m);
        result.Should().NotBeNull();
        // base = last + 60s, variance ∈ [0, 10s]
        result!.Value.Should().BeOnOrAfter(m.LastSuccessfulRun.Value.AddSeconds(60));
        result.Value.Should().BeOnOrBefore(m.LastSuccessfulRun.Value.AddSeconds(70));
    }

    [Test]
    public void ComputeNextScheduledRun_Cron_AppliesVariance()
    {
        var m = NewManifest();
        m.ScheduleType = ScheduleType.Cron;
        m.CronExpression = "0 * * * *";
        m.VarianceSeconds = 30;
        m.LastSuccessfulRun = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        var result = Invoke(m);
        result.Should().NotBeNull();
        result!.Value.Hour.Should().Be(13);
    }

    [Test]
    public void ComputeNextScheduledRun_OnceSchedule_ReturnsNull()
    {
        var m = NewManifest();
        m.ScheduleType = ScheduleType.Once;
        m.VarianceSeconds = 10;
        m.LastSuccessfulRun = DateTime.UtcNow;

        var result = Invoke(m);
        result.Should().BeNull();
    }

    [Test]
    public void ComputeNextScheduledRun_CronWithInvalidExpression_ReturnsNull()
    {
        var m = NewManifest();
        m.ScheduleType = ScheduleType.Cron;
        m.CronExpression = "garbage";
        m.VarianceSeconds = 10;
        m.LastSuccessfulRun = DateTime.UtcNow;

        var result = Invoke(m);
        result.Should().BeNull();
    }

    private static DateTime? Invoke(Manifest m)
    {
        var method = typeof(SchedulingHelpers).GetMethod(
            "ComputeNextScheduledRun",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static
        )!;
        return (DateTime?)method.Invoke(null, new object[] { m });
    }

    #endregion
}
