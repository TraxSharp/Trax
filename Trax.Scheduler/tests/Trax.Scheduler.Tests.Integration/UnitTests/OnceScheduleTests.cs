using AwesomeAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Core.Extensions;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Trains.ManifestManager.Utilities;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

[TestFixture]
public class OnceScheduleTests
{
    // The scheduling decisions take the time as an input; a fixed one makes them repeatable.
    private static readonly DateTime FixedNow = new(2026, 3, 10, 12, 0, 0, DateTimeKind.Utc);

    private SchedulerConfiguration? _config;
    private ILogger? _logger;

    [SetUp]
    public void SetUp()
    {
        _config = new SchedulerConfiguration();
        _logger = NullLoggerFactory.Instance.CreateLogger("test");
    }

    [Test]
    public void ShouldRunNow_WhenOnceManifestHasScheduledAtInPastAndNoLastSuccessfulRun_ReturnsTrue()
    {
        // Arrange
        var manifest = new Manifest
        {
            ExternalId = Guid.NewGuid().ToString("N"),
            Name = "TestTrain",
            ScheduleType = ScheduleType.Once,
            ScheduledAt = FixedNow.AddMinutes(-5),
            IsEnabled = true,
        };
        var now = FixedNow;

        // Act
        var result = SchedulingHelpers.ShouldRunNow(
            manifest,
            now,
            _config.NotNull(),
            _logger.NotNull()
        );

        // Assert
        result.Should().BeTrue("ScheduledAt is in the past and the manifest has never run");
    }

    [Test]
    public void ShouldRunNow_WhenOnceManifestHasScheduledAtInFuture_ReturnsFalse()
    {
        // Arrange
        var manifest = new Manifest
        {
            ExternalId = Guid.NewGuid().ToString("N"),
            Name = "TestTrain",
            ScheduleType = ScheduleType.Once,
            ScheduledAt = FixedNow.AddMinutes(30),
            IsEnabled = true,
        };
        var now = FixedNow;

        // Act
        var result = SchedulingHelpers.ShouldRunNow(
            manifest,
            now,
            _config.NotNull(),
            _logger.NotNull()
        );

        // Assert
        result.Should().BeFalse("ScheduledAt is in the future");
    }

    [Test]
    public void ShouldRunNow_WhenOnceManifestHasScheduledAtNull_ReturnsFalse()
    {
        // Arrange
        var manifest = new Manifest
        {
            ExternalId = Guid.NewGuid().ToString("N"),
            Name = "TestTrain",
            ScheduleType = ScheduleType.Once,
            ScheduledAt = null,
            IsEnabled = true,
        };
        var now = FixedNow;

        // Act
        var result = SchedulingHelpers.ShouldRunNow(
            manifest,
            now,
            _config.NotNull(),
            _logger.NotNull()
        );

        // Assert
        result.Should().BeFalse("ScheduledAt is null so the manifest cannot be scheduled");
    }

    [Test]
    public void ShouldRunNow_WhenOnceManifestAlreadyRan_ReturnsFalse()
    {
        // Arrange
        var manifest = new Manifest
        {
            ExternalId = Guid.NewGuid().ToString("N"),
            Name = "TestTrain",
            ScheduleType = ScheduleType.Once,
            ScheduledAt = FixedNow.AddMinutes(-30),
            LastSuccessfulRun = FixedNow.AddMinutes(-25),
            IsEnabled = true,
        };
        var now = FixedNow;

        // Act
        var result = SchedulingHelpers.ShouldRunNow(
            manifest,
            now,
            _config.NotNull(),
            _logger.NotNull()
        );

        // Assert
        result
            .Should()
            .BeFalse("the manifest has already run successfully (LastSuccessfulRun is set)");
    }
}
