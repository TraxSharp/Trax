using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Trains.ManifestManager.Utilities;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

[TestFixture]
public class ExclusionScheduleTests
{
    private SchedulerConfiguration _config = null!;
    private ILogger _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _config = new SchedulerConfiguration();
        _logger = NullLoggerFactory.Instance.CreateLogger("test");
    }

    // ── Cron + DaysOfWeek ───────────────────────────────────────────

    [Test]
    public void ShouldRunNow_WhenCronManifestExcludedByDayOfWeek_ReturnsFalse()
    {
        var saturday = new DateTime(2026, 3, 7, 3, 0, 0, DateTimeKind.Utc);
        var manifest = CreateCronManifest();
        manifest.SetExclusions([Exclude.DaysOfWeek(DayOfWeek.Saturday, DayOfWeek.Sunday)]);

        var result = SchedulingHelpers.ShouldRunNow(manifest, saturday, _config, _logger);

        result.Should().BeFalse("Saturday is excluded");
    }

    [Test]
    public void ShouldRunNow_WhenCronManifestNotExcluded_ReturnsTrue()
    {
        var monday = new DateTime(2026, 3, 2, 3, 0, 0, DateTimeKind.Utc);
        var manifest = CreateCronManifest();
        manifest.SetExclusions([Exclude.DaysOfWeek(DayOfWeek.Saturday, DayOfWeek.Sunday)]);

        var result = SchedulingHelpers.ShouldRunNow(manifest, monday, _config, _logger);

        result.Should().BeTrue("Monday is not excluded and manifest has never run");
    }

    // ── Interval + Dates ────────────────────────────────────────────

    [Test]
    public void ShouldRunNow_WhenIntervalManifestExcludedByDate_ReturnsFalse()
    {
        var christmas = new DateTime(2026, 12, 25, 12, 0, 0, DateTimeKind.Utc);
        var manifest = CreateIntervalManifest(intervalSeconds: 60);
        manifest.SetExclusions([Exclude.Dates(new DateOnly(2026, 12, 25))]);

        var result = SchedulingHelpers.ShouldRunNow(manifest, christmas, _config, _logger);

        result.Should().BeFalse("December 25 is excluded");
    }

    // ── TimeWindow ──────────────────────────────────────────────────

    [Test]
    public void ShouldRunNow_WhenManifestExcludedByTimeWindow_ReturnsFalse()
    {
        var maintenanceTime = new DateTime(2026, 3, 2, 3, 0, 0, DateTimeKind.Utc);
        var manifest = CreateCronManifest();
        manifest.SetExclusions([
            Exclude.TimeWindow(TimeOnly.Parse("02:00"), TimeOnly.Parse("04:00")),
        ]);

        var result = SchedulingHelpers.ShouldRunNow(manifest, maintenanceTime, _config, _logger);

        result.Should().BeFalse("3:00 AM is within the 2:00-4:00 exclusion window");
    }

    // ── Multiple Exclusions ─────────────────────────────────────────

    [Test]
    public void ShouldRunNow_WhenAnyExclusionMatches_ReturnsFalse()
    {
        var saturday = new DateTime(2026, 3, 7, 12, 0, 0, DateTimeKind.Utc);
        var manifest = CreateCronManifest();
        manifest.SetExclusions([
            Exclude.Dates(new DateOnly(2026, 12, 25)),
            Exclude.DaysOfWeek(DayOfWeek.Saturday, DayOfWeek.Sunday),
        ]);

        var result = SchedulingHelpers.ShouldRunNow(manifest, saturday, _config, _logger);

        result
            .Should()
            .BeFalse("Saturday matches the DaysOfWeek exclusion even though Dates doesn't match");
    }

    [Test]
    public void ShouldRunNow_WhenNoExclusionMatches_ReturnsTrue()
    {
        var monday = new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc);
        var manifest = CreateCronManifest();
        manifest.SetExclusions([
            Exclude.Dates(new DateOnly(2026, 12, 25)),
            Exclude.DaysOfWeek(DayOfWeek.Saturday, DayOfWeek.Sunday),
            Exclude.TimeWindow(TimeOnly.Parse("02:00"), TimeOnly.Parse("04:00")),
        ]);

        var result = SchedulingHelpers.ShouldRunNow(manifest, monday, _config, _logger);

        result
            .Should()
            .BeTrue("Monday at noon doesn't match any exclusion and manifest has never run");
    }

    // ── Empty Exclusions ────────────────────────────────────────────

    [Test]
    public void ShouldRunNow_WithEmptyExclusions_BehavesNormally()
    {
        var now = new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc);
        var manifest = CreateCronManifest();
        manifest.SetExclusions([]);

        var result = SchedulingHelpers.ShouldRunNow(manifest, now, _config, _logger);

        result.Should().BeTrue("no exclusions should not block scheduling");
    }

    // ── Once + Exclusion ────────────────────────────────────────────

    [Test]
    public void ShouldRunNow_WhenOnceManifestExcluded_ReturnsFalse()
    {
        var saturday = new DateTime(2026, 3, 7, 12, 0, 0, DateTimeKind.Utc);
        var manifest = new Manifest
        {
            ExternalId = Guid.NewGuid().ToString("N"),
            Name = "TestTrain",
            ScheduleType = ScheduleType.Once,
            ScheduledAt = DateTime.UtcNow.AddMinutes(-5),
            IsEnabled = true,
        };
        manifest.SetExclusions([Exclude.DaysOfWeek(DayOfWeek.Saturday)]);

        var result = SchedulingHelpers.ShouldRunNow(manifest, saturday, _config, _logger);

        result.Should().BeFalse("Saturday is excluded even for Once manifests");
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static Manifest CreateCronManifest() =>
        new()
        {
            ExternalId = Guid.NewGuid().ToString("N"),
            Name = "TestTrain",
            ScheduleType = ScheduleType.Cron,
            CronExpression = "0 3 * * *",
            IsEnabled = true,
        };

    private static Manifest CreateIntervalManifest(int intervalSeconds) =>
        new()
        {
            ExternalId = Guid.NewGuid().ToString("N"),
            Name = "TestTrain",
            ScheduleType = ScheduleType.Interval,
            IntervalSeconds = intervalSeconds,
            IsEnabled = true,
        };
}
