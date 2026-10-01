using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Trains.ManifestManager.Utilities;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// A DoNothing cron that has been down longer than its misfire threshold fires only when now is
/// within the threshold of the latest occurrence. That occurrence is found however long the gap
/// since the last success is, not only when the gap holds fewer than some number of occurrences.
/// </summary>
[TestFixture]
public class SchedulingHelpersCronCatchUpTests
{
    private SchedulerConfiguration _config = null!;
    private ILogger _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _config = new SchedulerConfiguration();
        _logger = NullLoggerFactory.Instance.CreateLogger("test");
    }

    [Test]
    public void A_secondly_cron_last_run_two_days_ago_is_queued()
    {
        var now = new DateTime(2026, 3, 4, 12, 0, 0, DateTimeKind.Utc);
        var manifest = DoNothingCron("* * * * * *", lastSuccess: now.AddDays(-2));

        SchedulingHelpers.ShouldRunNow(manifest, now, _config, _logger).Should().BeTrue();
    }

    [Test]
    public void A_minutely_cron_a_year_behind_fires_within_the_threshold_of_the_latest_minute()
    {
        var now = new DateTime(2026, 3, 4, 12, 0, 30, DateTimeKind.Utc);
        var manifest = DoNothingCron("* * * * *", lastSuccess: now.AddYears(-1));

        SchedulingHelpers.ShouldRunNow(manifest, now, _config, _logger).Should().BeTrue();
    }

    [Test]
    public void A_minutely_cron_a_year_behind_waits_when_past_the_threshold_of_the_latest_minute()
    {
        var now = new DateTime(2026, 3, 4, 12, 0, 30, DateTimeKind.Utc);
        var manifest = DoNothingCron("* * * * *", lastSuccess: now.AddYears(-1));
        manifest.MisfireThresholdSeconds = 10;

        SchedulingHelpers.ShouldRunNow(manifest, now, _config, _logger).Should().BeFalse();
    }

    [TestCase(20, true)]
    [TestCase(59, true)]
    [TestCase(61, false)]
    [TestCase(7200, false)]
    public void A_daily_cron_ten_days_behind_fires_only_near_todays_occurrence(
        int secondsAfterOccurrence,
        bool expected
    )
    {
        var occurrence = new DateTime(2026, 3, 4, 3, 0, 0, DateTimeKind.Utc);
        var now = occurrence.AddSeconds(secondsAfterOccurrence);
        var manifest = DoNothingCron("0 3 * * *", lastSuccess: occurrence.AddDays(-10));

        SchedulingHelpers.ShouldRunNow(manifest, now, _config, _logger).Should().Be(expected);
    }

    [Test]
    public void The_latest_occurrence_is_exactly_the_last_one_at_or_before_now()
    {
        var now = new DateTime(2026, 3, 4, 12, 7, 42, DateTimeKind.Utc);
        var parsed = Services.Scheduling.CronParser.Parse("*/15 * * * * *");

        SchedulingHelpers
            .LatestOccurrence(parsed, after: now.AddYears(-1), now)
            .Should()
            .Be(new DateTime(2026, 3, 4, 12, 7, 30, DateTimeKind.Utc));
        SchedulingHelpers
            .LatestOccurrence(parsed, after: now.AddSeconds(-10), now)
            .Should()
            .BeNull("no occurrence falls after the lower bound");
        SchedulingHelpers
            .LatestOccurrence(parsed, after: now.AddYears(-1), now.AddSeconds(-12))
            .Should()
            .Be(new DateTime(2026, 3, 4, 12, 7, 30, DateTimeKind.Utc), "now itself counts");
    }

    [Test]
    public void A_cron_that_never_occurs_has_no_latest_occurrence()
    {
        var now = new DateTime(2026, 3, 4, 12, 0, 0, DateTimeKind.Utc);
        var parsed = Services.Scheduling.CronParser.Parse("0 0 0 30 2 *");

        SchedulingHelpers
            .LatestOccurrence(parsed, after: now.AddYears(-5), now)
            .Should()
            .BeNull("February has no 30th, so there is nothing to catch up on");
    }

    private static Manifest DoNothingCron(string cron, DateTime lastSuccess) =>
        new()
        {
            ExternalId = Guid.NewGuid().ToString("N"),
            Name = "TestTrain",
            ScheduleType = ScheduleType.Cron,
            CronExpression = cron,
            IsEnabled = true,
            MisfirePolicy = MisfirePolicy.DoNothing,
            LastSuccessfulRun = lastSuccess,
        };
}
