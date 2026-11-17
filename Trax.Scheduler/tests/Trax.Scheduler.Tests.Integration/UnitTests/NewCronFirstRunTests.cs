using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.Scheduling;
using Trax.Scheduler.Trains.ManifestManager.Utilities;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

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
