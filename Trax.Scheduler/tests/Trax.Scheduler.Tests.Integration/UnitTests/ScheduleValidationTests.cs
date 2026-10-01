using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Scheduling;
using Trax.Scheduler.Trains.ManifestManager.Utilities;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// A schedule is checked where it is written, so a cron that can never fire or an interval of
/// zero fails at the call that states it instead of being stored and silently misbehaving.
/// </summary>
[TestFixture]
public class ScheduleValidationTests
{
    [Test]
    public void An_hour_out_of_range_is_refused()
    {
        var act = () => Cron.Daily(hour: 25);

        act.Should().Throw<FormatException>();
    }

    [TestCase("not a cron")]
    [TestCase("* * *")]
    [TestCase("61 * * * *")]
    [TestCase("0 0 31 2 * 9")]
    [TestCase("")]
    public void An_invalid_cron_expression_is_refused(string expression)
    {
        var act = () => Schedule.FromCron(expression);

        act.Should().Throw<FormatException>();
    }

    [TestCase("0 0 30 2 *")]
    [TestCase("0 0 31 4 *")]
    [TestCase("0 0 31 2,4,6,9,11 *")]
    public void A_cron_that_can_never_fire_is_refused(string expression)
    {
        var act = () => Schedule.FromCron(expression);

        act.Should().Throw<FormatException>().WithMessage("*never*");
    }

    [Test]
    public void A_cron_that_fires_only_in_leap_years_is_accepted()
    {
        var act = () => Schedule.FromCron("0 0 29 2 *");

        act.Should().NotThrow();
    }

    [Test]
    public void A_raw_expression_is_checked_too()
    {
        var act = () => Cron.Expression("0 3 * * * * *");

        act.Should().Throw<FormatException>();
    }

    [TestCase("0 3 * * *")]
    [TestCase("*/15 * * * * *")]
    [TestCase(" 0 3 * * 1-5 ")]
    public void A_valid_cron_expression_is_kept_as_written(string expression)
    {
        Schedule.FromCron(expression).CronExpression.Should().Be(expression);
    }

    [Test]
    public void Every_zero_seconds_is_refused()
    {
        var act = () => Every.Seconds(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void A_sub_second_interval_is_refused()
    {
        var act = () => Schedule.FromInterval(TimeSpan.FromMilliseconds(500));

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void A_negative_interval_is_refused()
    {
        var act = () => Every.Minutes(-5);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void One_second_is_the_shortest_interval()
    {
        Every.Seconds(1).Interval.Should().Be(TimeSpan.FromSeconds(1));
    }
}

/// <summary>
/// A new cron's first run is its first occurrence after it was scheduled, not the next poll.
/// </summary>
[TestFixture]
public class NewCronFirstRunTests
{
    private readonly SchedulerConfiguration _config = new();
    private readonly ILogger _logger = NullLoggerFactory.Instance.CreateLogger("test");

    [Test]
    public void A_never_run_cron_is_not_due_before_its_first_occurrence()
    {
        var now = new DateTime(2026, 3, 4, 14, 0, 0, DateTimeKind.Utc);
        var manifest = NeverRunCron(
            "0 3 * * *",
            firstOccurrence: new DateTime(2026, 3, 5, 3, 0, 0, DateTimeKind.Utc)
        );

        SchedulingHelpers.ShouldRunNow(manifest, now, _config, _logger).Should().BeFalse();
    }

    [Test]
    public void A_never_run_cron_is_due_at_its_first_occurrence()
    {
        var now = new DateTime(2026, 3, 5, 3, 0, 5, DateTimeKind.Utc);
        var manifest = NeverRunCron(
            "0 3 * * *",
            firstOccurrence: new DateTime(2026, 3, 5, 3, 0, 0, DateTimeKind.Utc)
        );

        SchedulingHelpers.ShouldRunNow(manifest, now, _config, _logger).Should().BeTrue();
    }

    [Test]
    public void A_never_run_do_nothing_cron_long_past_its_first_occurrence_waits_for_the_next()
    {
        var now = new DateTime(2026, 3, 8, 14, 0, 0, DateTimeKind.Utc);
        var manifest = NeverRunCron(
            "0 3 * * *",
            firstOccurrence: new DateTime(2026, 3, 5, 3, 0, 0, DateTimeKind.Utc)
        );
        manifest.MisfirePolicy = MisfirePolicy.DoNothing;

        SchedulingHelpers.ShouldRunNow(manifest, now, _config, _logger).Should().BeFalse();
        SchedulingHelpers
            .ShouldRunNow(manifest, now.Date.AddDays(1).AddHours(3), _config, _logger)
            .Should()
            .BeTrue();
    }

    private static Manifest NeverRunCron(string cron, DateTime firstOccurrence) =>
        new()
        {
            ExternalId = Guid.NewGuid().ToString("N"),
            Name = "TestTrain",
            ScheduleType = ScheduleType.Cron,
            CronExpression = cron,
            IsEnabled = true,
            NextScheduledRun = firstOccurrence,
        };
}
