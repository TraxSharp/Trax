using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Trains.ManifestManager.Utilities;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

[TestFixture]
public class SchedulingHelpersCronTests
{
    private SchedulerConfiguration _config = null!;
    private ILogger _logger = null!;

    [SetUp]
    public void SetUp()
    {
        _config = new SchedulerConfiguration();
        _logger = NullLoggerFactory.Instance.CreateLogger("test");
    }

    // ── IsTimeForCron — 5-field ──────────────────────────────────────

    [Test]
    public void IsTimeForCron_5Field_WhenDue_ReturnsTrue()
    {
        var lastRun = new DateTime(2026, 3, 2, 2, 0, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 3, 2, 3, 0, 0, DateTimeKind.Utc);

        SchedulingHelpers.IsTimeForCron(lastRun, "0 3 * * *", now).Should().BeTrue();
    }

    [Test]
    public void IsTimeForCron_5Field_WhenNotDue_ReturnsFalse()
    {
        var lastRun = new DateTime(2026, 3, 2, 2, 0, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 3, 2, 2, 30, 0, DateTimeKind.Utc);

        SchedulingHelpers.IsTimeForCron(lastRun, "0 3 * * *", now).Should().BeFalse();
    }

    [Test]
    public void IsTimeForCron_NullLastRun_ReturnsTrue()
    {
        SchedulingHelpers.IsTimeForCron(null, "0 3 * * *", DateTime.UtcNow).Should().BeTrue();
    }

    [Test]
    public void IsTimeForCron_InvalidExpression_ReturnsFalse()
    {
        var lastRun = new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc);
        SchedulingHelpers.IsTimeForCron(lastRun, "invalid", DateTime.UtcNow).Should().BeFalse();
    }

    // ── IsTimeForCron — 6-field ──────────────────────────────────────

    [Test]
    public void IsTimeForCron_6Field_EveryMinuteAtSecond30_WhenDue_ReturnsTrue()
    {
        var lastRun = new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 3, 2, 12, 0, 30, DateTimeKind.Utc);

        SchedulingHelpers.IsTimeForCron(lastRun, "30 * * * * *", now).Should().BeTrue();
    }

    [Test]
    public void IsTimeForCron_6Field_Every15Seconds_WhenDue_ReturnsTrue()
    {
        var lastRun = new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 3, 2, 12, 0, 15, DateTimeKind.Utc);

        SchedulingHelpers.IsTimeForCron(lastRun, "*/15 * * * * *", now).Should().BeTrue();
    }

    [Test]
    public void IsTimeForCron_6Field_Every15Seconds_WhenNotDue_ReturnsFalse()
    {
        var lastRun = new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 3, 2, 12, 0, 10, DateTimeKind.Utc);

        SchedulingHelpers.IsTimeForCron(lastRun, "*/15 * * * * *", now).Should().BeFalse();
    }

    [Test]
    public void IsTimeForCron_6Field_NullLastRun_ReturnsTrue()
    {
        SchedulingHelpers.IsTimeForCron(null, "*/15 * * * * *", DateTime.UtcNow).Should().BeTrue();
    }

    // ── ShouldRunNow — 6-field cron ──────────────────────────────────

    [Test]
    public void ShouldRunNow_6FieldCron_NeverRun_ReturnsTrue()
    {
        var manifest = CreateCronManifest("*/30 * * * * *");

        SchedulingHelpers
            .ShouldRunNow(manifest, DateTime.UtcNow, _config, _logger)
            .Should()
            .BeTrue();
    }

    [Test]
    public void ShouldRunNow_6FieldCron_WhenDue_ReturnsTrue()
    {
        var now = new DateTime(2026, 3, 2, 12, 1, 0, DateTimeKind.Utc);
        var manifest = CreateCronManifest("*/30 * * * * *");
        manifest.LastSuccessfulRun = new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc);

        SchedulingHelpers.ShouldRunNow(manifest, now, _config, _logger).Should().BeTrue();
    }

    [Test]
    public void ShouldRunNow_6FieldCron_NotYetDue_ReturnsFalse()
    {
        var now = new DateTime(2026, 3, 2, 12, 0, 10, DateTimeKind.Utc);
        var manifest = CreateCronManifest("*/30 * * * * *");
        manifest.LastSuccessfulRun = new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc);

        SchedulingHelpers.ShouldRunNow(manifest, now, _config, _logger).Should().BeFalse();
    }

    [Test]
    public void ShouldRunNow_6FieldCron_InvalidExpression_ReturnsFalse()
    {
        var manifest = CreateCronManifest("invalid-cron");
        manifest.LastSuccessfulRun = new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc);

        SchedulingHelpers
            .ShouldRunNow(manifest, DateTime.UtcNow, _config, _logger)
            .Should()
            .BeFalse();
    }

    // ── ShouldRunNow — 5-field precision (replaces heuristic) ────────

    [Test]
    public void ShouldRunNow_5FieldCron_PreciseNextOccurrence_WhenDue_ReturnsTrue()
    {
        // "0 */6 * * *" = every 6 hours. Last ran at 6:00, now is 12:00 → due
        var manifest = CreateCronManifest("0 */6 * * *");
        manifest.LastSuccessfulRun = new DateTime(2026, 3, 2, 6, 0, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc);

        SchedulingHelpers.ShouldRunNow(manifest, now, _config, _logger).Should().BeTrue();
    }

    [Test]
    public void ShouldRunNow_5FieldCron_PreciseNextOccurrence_WhenNotDue_ReturnsFalse()
    {
        // "0 */6 * * *" = every 6 hours. Last ran at 6:00, now is 10:00 → not due (next at 12:00)
        var manifest = CreateCronManifest("0 */6 * * *");
        manifest.LastSuccessfulRun = new DateTime(2026, 3, 2, 6, 0, 0, DateTimeKind.Utc);
        var now = new DateTime(2026, 3, 2, 10, 0, 0, DateTimeKind.Utc);

        SchedulingHelpers.ShouldRunNow(manifest, now, _config, _logger).Should().BeFalse();
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static Manifest CreateCronManifest(string cronExpression) =>
        new()
        {
            ExternalId = Guid.NewGuid().ToString("N"),
            Name = "TestTrain",
            ScheduleType = ScheduleType.Cron,
            CronExpression = cronExpression,
            IsEnabled = true,
        };
}
