using FluentAssertions;
using Trax.Effect.Enums;
using Trax.Scheduler.Services.Scheduling;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

[TestFixture]
public class ScheduleTests
{
    #region FromInterval

    [Test]
    public void FromInterval_SetsTypeAndInterval()
    {
        // Act
        var schedule = Schedule.FromInterval(TimeSpan.FromMinutes(5));

        // Assert
        schedule.Type.Should().Be(ScheduleType.Interval);
        schedule.Interval.Should().Be(TimeSpan.FromMinutes(5));
        schedule.CronExpression.Should().BeNull();
    }

    #endregion

    #region FromCron

    [Test]
    public void FromCron_SetsTypeAndExpression()
    {
        // Act
        var schedule = Schedule.FromCron("0 3 * * *");

        // Assert
        schedule.Type.Should().Be(ScheduleType.Cron);
        schedule.CronExpression.Should().Be("0 3 * * *");
        schedule.Interval.Should().BeNull();
    }

    #endregion

    #region ToCronExpression

    [Test]
    public void ToCronExpression_CronType_ReturnsExpression()
    {
        // Arrange
        var schedule = Schedule.FromCron("30 2 * * 1");

        // Act
        var cron = schedule.ToCronExpression();

        // Assert
        cron.Should().Be("30 2 * * 1");
    }

    [Test]
    public void ToCronExpression_1Minute_ReturnsEveryMinute()
    {
        // Arrange
        var schedule = Schedule.FromInterval(TimeSpan.FromMinutes(1));

        // Act
        var cron = schedule.ToCronExpression();

        // Assert
        cron.Should().Be("* * * * *");
    }

    [Test]
    public void ToCronExpression_5Minutes_ReturnsCorrect()
    {
        // Arrange
        var schedule = Schedule.FromInterval(TimeSpan.FromMinutes(5));

        // Act
        var cron = schedule.ToCronExpression();

        // Assert
        cron.Should().Be("*/5 * * * *");
    }

    [Test]
    public void ToCronExpression_60Minutes_ReturnsHourly()
    {
        // Arrange
        var schedule = Schedule.FromInterval(TimeSpan.FromMinutes(60));

        // Act
        var cron = schedule.ToCronExpression();

        // Assert
        cron.Should().Be("0 * * * *");
    }

    [Test]
    public void ToCronExpression_120Minutes_ReturnsEvery2Hours()
    {
        // Arrange
        var schedule = Schedule.FromInterval(TimeSpan.FromMinutes(120));

        // Act
        var cron = schedule.ToCronExpression();

        // Assert
        cron.Should().Be("0 */2 * * *");
    }

    [Test]
    public void ToCronExpression_NullInterval_DefaultsToEveryMinute()
    {
        // Arrange — create an interval schedule with null Interval via record syntax
        var schedule = new Schedule { Type = ScheduleType.Interval, Interval = null };

        // Act
        var cron = schedule.ToCronExpression();

        // Assert
        cron.Should().Be("* * * * *");
    }

    [Test]
    public void ToCronExpression_45Minutes_ApproximatesToClosestDivisor()
    {
        // Arrange — 45 doesn't divide 60 evenly, should approximate to closest divisor (30)
        var schedule = Schedule.FromInterval(TimeSpan.FromMinutes(45));

        // Act
        var cron = schedule.ToCronExpression();

        // Assert
        cron.Should().Be("*/30 * * * *");
    }

    [Test]
    public void Schedule_RecordEquality_Works()
    {
        // Arrange
        var a = Schedule.FromInterval(TimeSpan.FromMinutes(5));
        var b = Schedule.FromInterval(TimeSpan.FromMinutes(5));

        // Assert
        a.Should().Be(b);
    }

    #endregion
}
