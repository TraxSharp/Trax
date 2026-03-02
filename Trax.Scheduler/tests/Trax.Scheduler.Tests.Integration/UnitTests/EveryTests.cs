using FluentAssertions;
using Trax.Effect.Enums;
using Trax.Scheduler.Services.Scheduling;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

[TestFixture]
public class EveryTests
{
    [Test]
    public void Seconds_CreatesIntervalSchedule()
    {
        // Act
        var schedule = Every.Seconds(30);

        // Assert
        schedule.Type.Should().Be(ScheduleType.Interval);
        schedule.Interval.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Test]
    public void Minutes_CreatesIntervalSchedule()
    {
        // Act
        var schedule = Every.Minutes(5);

        // Assert
        schedule.Type.Should().Be(ScheduleType.Interval);
        schedule.Interval.Should().Be(TimeSpan.FromMinutes(5));
    }

    [Test]
    public void Hours_CreatesIntervalSchedule()
    {
        // Act
        var schedule = Every.Hours(2);

        // Assert
        schedule.Type.Should().Be(ScheduleType.Interval);
        schedule.Interval.Should().Be(TimeSpan.FromHours(2));
    }

    [Test]
    public void Days_CreatesIntervalSchedule()
    {
        // Act
        var schedule = Every.Days(1);

        // Assert
        schedule.Type.Should().Be(ScheduleType.Interval);
        schedule.Interval.Should().Be(TimeSpan.FromDays(1));
    }
}
