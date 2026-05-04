using FluentAssertions;
using NUnit.Framework;
using Radzen;
using Trax.Dashboard.Utilities;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;

namespace Trax.Dashboard.Tests.Integration.UnitTests;

[TestFixture]
public class DashboardFormattersCoverageTests
{
    #region ShortName

    [Test]
    public void ShortName_DottedName_ReturnsLastSegment()
    {
        DashboardFormatters.ShortName("Foo.Bar.Baz").Should().Be("Baz");
    }

    [Test]
    public void ShortName_NoDot_ReturnsAsIs()
    {
        DashboardFormatters.ShortName("Plain").Should().Be("Plain");
    }

    [Test]
    public void ShortName_Empty_ReturnsEmpty()
    {
        DashboardFormatters.ShortName("").Should().Be("");
    }

    #endregion

    #region FormatDuration

    [Test]
    public void FormatDuration_SubSecond_ReturnsMs()
    {
        DashboardFormatters.FormatDuration(750).Should().Be("750ms");
    }

    [Test]
    public void FormatDuration_SubMinute_ReturnsSeconds()
    {
        DashboardFormatters.FormatDuration(5_000).Should().Be("5.0s");
    }

    [Test]
    public void FormatDuration_OverMinute_ReturnsMinutes()
    {
        DashboardFormatters.FormatDuration(120_000).Should().Be("2.0m");
    }

    [Test]
    public void FormatDuration_MetadataNoEndTime_ReturnsDash()
    {
        var meta = NewMetadata();
        meta.EndTime = null;

        DashboardFormatters.FormatDuration(meta).Should().Be("—");
    }

    [Test]
    public void FormatDuration_MetadataWithEndTime_ReturnsFormatted()
    {
        var meta = NewMetadata();
        meta.StartTime = DateTime.UtcNow;
        meta.EndTime = meta.StartTime.AddSeconds(2);

        DashboardFormatters.FormatDuration(meta).Should().Contain("s");
    }

    #endregion

    #region FormatSchedule

    [Test]
    public void FormatSchedule_Cron_ReturnsExpression()
    {
        var m = NewManifest();
        m.ScheduleType = ScheduleType.Cron;
        m.CronExpression = "0 * * * *";

        DashboardFormatters.FormatSchedule(m).Should().Be("0 * * * *");
    }

    [Test]
    public void FormatSchedule_CronNullExpression_ReturnsDash()
    {
        var m = NewManifest();
        m.ScheduleType = ScheduleType.Cron;

        DashboardFormatters.FormatSchedule(m).Should().Be("—");
    }

    [Test]
    public void FormatSchedule_IntervalSeconds_FormatsAsSeconds()
    {
        var m = NewManifest();
        m.ScheduleType = ScheduleType.Interval;
        m.IntervalSeconds = 30;

        DashboardFormatters.FormatSchedule(m).Should().Be("Every 30s");
    }

    [Test]
    public void FormatSchedule_IntervalMinutes_FormatsAsMinutes()
    {
        var m = NewManifest();
        m.ScheduleType = ScheduleType.Interval;
        m.IntervalSeconds = 600;

        DashboardFormatters.FormatSchedule(m).Should().Be("Every 10m");
    }

    [Test]
    public void FormatSchedule_IntervalHours_FormatsAsHours()
    {
        var m = NewManifest();
        m.ScheduleType = ScheduleType.Interval;
        m.IntervalSeconds = 7200;

        DashboardFormatters.FormatSchedule(m).Should().Be("Every 2h");
    }

    [Test]
    public void FormatSchedule_IntervalNullSeconds_ReturnsDash()
    {
        var m = NewManifest();
        m.ScheduleType = ScheduleType.Interval;

        DashboardFormatters.FormatSchedule(m).Should().Be("—");
    }

    [Test]
    public void FormatSchedule_OnceWithFutureTime_ReturnsFormattedTime()
    {
        var m = NewManifest();
        m.ScheduleType = ScheduleType.Once;
        m.ScheduledAt = DateTime.UtcNow.AddDays(1);

        DashboardFormatters.FormatSchedule(m).Should().StartWith("Once at");
    }

    [Test]
    public void FormatSchedule_OncePastTime_ReturnsFired()
    {
        var m = NewManifest();
        m.ScheduleType = ScheduleType.Once;
        m.ScheduledAt = DateTime.UtcNow.AddDays(-1);

        DashboardFormatters.FormatSchedule(m).Should().Be("Once (fired)");
    }

    [Test]
    public void FormatSchedule_OnceNullTime_ReturnsNoTimeSet()
    {
        var m = NewManifest();
        m.ScheduleType = ScheduleType.Once;

        DashboardFormatters.FormatSchedule(m).Should().Contain("no time set");
    }

    [Test]
    public void FormatSchedule_None_ReturnsTypeName()
    {
        var m = NewManifest();
        m.ScheduleType = ScheduleType.None;

        DashboardFormatters.FormatSchedule(m).Should().Be("None");
    }

    #endregion

    #region FormatJson

    [Test]
    public void FormatJson_ValidJson_ReturnsIndented()
    {
        var formatted = DashboardFormatters.FormatJson("""{"a":1,"b":2}""");

        formatted.Should().Contain("\"a\"").And.Contain("\n");
    }

    [Test]
    public void FormatJson_InvalidJson_ReturnsOriginal()
    {
        DashboardFormatters.FormatJson("not json").Should().Be("not json");
    }

    #endregion

    #region Badge styles

    [TestCase(TrainState.Completed, BadgeStyle.Success)]
    [TestCase(TrainState.Failed, BadgeStyle.Danger)]
    [TestCase(TrainState.InProgress, BadgeStyle.Info)]
    [TestCase(TrainState.Pending, BadgeStyle.Warning)]
    [TestCase(TrainState.Cancelled, BadgeStyle.Warning)]
    public void GetStateBadgeStyle_KnownStates_MapToExpectedStyle(
        TrainState state,
        BadgeStyle expected
    )
    {
        DashboardFormatters.GetStateBadgeStyle(state).Should().Be(expected);
    }

    [Test]
    public void GetStateBadgeStyle_Unknown_ReturnsLight()
    {
        DashboardFormatters.GetStateBadgeStyle((TrainState)999).Should().Be(BadgeStyle.Light);
    }

    [TestCase(DeadLetterStatus.AwaitingIntervention, BadgeStyle.Warning)]
    [TestCase(DeadLetterStatus.Retried, BadgeStyle.Info)]
    [TestCase(DeadLetterStatus.Acknowledged, BadgeStyle.Success)]
    public void GetDeadLetterStatusBadgeStyle_Known_MapsAsExpected(
        DeadLetterStatus status,
        BadgeStyle expected
    )
    {
        DashboardFormatters.GetDeadLetterStatusBadgeStyle(status).Should().Be(expected);
    }

    [Test]
    public void GetDeadLetterStatusBadgeStyle_Unknown_ReturnsLight()
    {
        DashboardFormatters
            .GetDeadLetterStatusBadgeStyle((DeadLetterStatus)999)
            .Should()
            .Be(BadgeStyle.Light);
    }

    [TestCase(WorkQueueStatus.Queued, BadgeStyle.Info)]
    [TestCase(WorkQueueStatus.Dispatched, BadgeStyle.Success)]
    [TestCase(WorkQueueStatus.Cancelled, BadgeStyle.Warning)]
    public void GetWorkQueueStatusBadgeStyle_Known_MapsAsExpected(
        WorkQueueStatus status,
        BadgeStyle expected
    )
    {
        DashboardFormatters.GetWorkQueueStatusBadgeStyle(status).Should().Be(expected);
    }

    [Test]
    public void GetWorkQueueStatusBadgeStyle_Unknown_ReturnsLight()
    {
        DashboardFormatters
            .GetWorkQueueStatusBadgeStyle((WorkQueueStatus)999)
            .Should()
            .Be(BadgeStyle.Light);
    }

    #endregion

    private static Metadata NewMetadata() =>
        Metadata.Create(
            new CreateMetadata
            {
                Name = "Trax.X.MyTrain",
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );

    private static Manifest NewManifest() =>
        Manifest.Create(new CreateManifest { Name = typeof(SomeFakeTrain) });

    private class SomeFakeTrain { }
}
