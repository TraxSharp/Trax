using FluentAssertions;
using Trax.Effect.Enums;
using Trax.Scheduler.Services.Scheduling;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

[TestFixture]
public class CronTests
{
    [Test]
    public void Minutely_ReturnsCorrectCron()
    {
        // Act
        var schedule = Cron.Minutely();

        // Assert
        schedule.Type.Should().Be(ScheduleType.Cron);
        schedule.CronExpression.Should().Be("* * * * *");
    }

    [Test]
    public void Hourly_DefaultMinute_ReturnsCorrect()
    {
        // Act
        var schedule = Cron.Hourly();

        // Assert
        schedule.CronExpression.Should().Be("0 * * * *");
    }

    [Test]
    public void Hourly_SpecificMinute_ReturnsCorrect()
    {
        // Act
        var schedule = Cron.Hourly(30);

        // Assert
        schedule.CronExpression.Should().Be("30 * * * *");
    }

    [Test]
    public void Daily_DefaultTime_ReturnsMidnight()
    {
        // Act
        var schedule = Cron.Daily();

        // Assert
        schedule.CronExpression.Should().Be("0 0 * * *");
    }

    [Test]
    public void Daily_SpecificTime_ReturnsCorrect()
    {
        // Act
        var schedule = Cron.Daily(hour: 3, minute: 15);

        // Assert
        schedule.CronExpression.Should().Be("15 3 * * *");
    }

    [Test]
    public void Weekly_Monday_ReturnsCorrect()
    {
        // Act
        var schedule = Cron.Weekly(DayOfWeek.Monday, hour: 9, minute: 30);

        // Assert
        schedule.CronExpression.Should().Be("30 9 * * 1");
    }

    [Test]
    public void Monthly_15thAt6AM_ReturnsCorrect()
    {
        // Act
        var schedule = Cron.Monthly(day: 15, hour: 6);

        // Assert
        schedule.CronExpression.Should().Be("0 6 15 * *");
    }

    [Test]
    public void Expression_PassthroughCron()
    {
        // Arrange
        var expression = "0 */6 * * *";

        // Act
        var schedule = Cron.Expression(expression);

        // Assert
        schedule.Type.Should().Be(ScheduleType.Cron);
        schedule.CronExpression.Should().Be(expression);
    }

    // ── 6-field (seconds granularity) ────────────────────────────────

    [Test]
    public void Secondly_Returns6FieldCron()
    {
        var schedule = Cron.Secondly();

        schedule.Type.Should().Be(ScheduleType.Cron);
        schedule.CronExpression.Should().Be("* * * * * *");
    }

    [Test]
    public void Minutely_WithSecond_Returns6FieldCron()
    {
        var schedule = Cron.Minutely(30);

        schedule.CronExpression.Should().Be("30 * * * * *");
    }

    [Test]
    public void Hourly_WithSecond_Returns6FieldCron()
    {
        var schedule = Cron.Hourly(minute: 15, second: 30);

        schedule.CronExpression.Should().Be("30 15 * * * *");
    }

    [Test]
    public void Hourly_WithZeroSecond_Returns5FieldCron()
    {
        var schedule = Cron.Hourly(minute: 15, second: 0);

        schedule.CronExpression.Should().Be("15 * * * *");
    }

    [Test]
    public void Daily_WithSecond_Returns6FieldCron()
    {
        var schedule = Cron.Daily(hour: 3, minute: 15, second: 45);

        schedule.CronExpression.Should().Be("45 15 3 * * *");
    }

    [Test]
    public void Daily_WithZeroSecond_Returns5FieldCron()
    {
        var schedule = Cron.Daily(hour: 3, minute: 15, second: 0);

        schedule.CronExpression.Should().Be("15 3 * * *");
    }

    [Test]
    public void Weekly_WithSecond_Returns6FieldCron()
    {
        var schedule = Cron.Weekly(DayOfWeek.Monday, hour: 9, minute: 30, second: 15);

        schedule.CronExpression.Should().Be("15 30 9 * * 1");
    }

    [Test]
    public void Monthly_WithSecond_Returns6FieldCron()
    {
        var schedule = Cron.Monthly(day: 15, hour: 6, minute: 0, second: 10);

        schedule.CronExpression.Should().Be("10 0 6 15 * *");
    }

    [Test]
    public void Expression_Passthrough6FieldCron()
    {
        var expression = "*/15 * * * * *";

        var schedule = Cron.Expression(expression);

        schedule.Type.Should().Be(ScheduleType.Cron);
        schedule.CronExpression.Should().Be(expression);
    }
}
