using AwesomeAssertions;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Scheduler.Services.Scheduling;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

[TestFixture]
public class ScheduleTextTests
{
    [TestCase("0 2 * * *", "Daily at 02:00 UTC")]
    [TestCase("30 14 * * *", "Daily at 14:30 UTC")]
    [TestCase("0 2,14 * * *", "Daily at 02:00 and 14:00 UTC")]
    [TestCase("* * * * *", "Every minute")]
    [TestCase("*/5 * * * *", "Every 5 minutes")]
    [TestCase("15 * * * *", "Every hour at :15")]
    [TestCase("0 */6 * * *", "Every 6 hours at :00")]
    [TestCase("0 9 * * 1-5", "Weekdays at 09:00 UTC")]
    [TestCase("0 10 * * 0,6", "Weekends at 10:00 UTC")]
    [TestCase("0 9 * * 1", "Every Monday at 09:00 UTC")]
    [TestCase("0 9 * * 1,4", "Every Monday and Thursday at 09:00 UTC")]
    [TestCase("0 0 15 * *", "Monthly on the 15th at 00:00 UTC")]
    [TestCase("0 0 1 * *", "Monthly on the 1st at 00:00 UTC")]
    [TestCase("0 0 25 12 *", "Yearly on December 25 at 00:00 UTC")]
    [TestCase("* * * * * *", "Every second")]
    [TestCase("*/15 * * * * *", "Every 15 seconds")]
    [TestCase("0 0 3 * * *", "Daily at 03:00 UTC")]
    [TestCase("30 0 3 * * *", "Daily at 03:00:30 UTC")]
    [TestCase("0 9-17 * * 1-5", "Cron 0 9-17 * * 1-5 (UTC)")]
    [TestCase("not a cron", "Cron not a cron (UTC)")]
    public void DescribeCron_SaysTheScheduleInWords(string expression, string expected)
    {
        ScheduleText.DescribeCron(expression).Should().Be(expected);
    }

    [TestCase(1, "Every second")]
    [TestCase(30, "Every 30 seconds")]
    [TestCase(90, "Every 90 seconds")]
    [TestCase(60, "Every minute")]
    [TestCase(300, "Every 5 minutes")]
    [TestCase(5400, "Every 90 minutes")]
    [TestCase(3600, "Every hour")]
    [TestCase(7200, "Every 2 hours")]
    [TestCase(86400, "Every day")]
    [TestCase(172800, "Every 2 days")]
    public void DescribeInterval_SaysTheIntervalInItsLargestWholeUnit(int seconds, string expected)
    {
        ScheduleText.DescribeInterval(seconds).Should().Be(expected);
    }

    [Test]
    public void Describe_ASchedule_SaysItsVariance()
    {
        ScheduleText
            .Describe(Every.Minutes(5).WithVariance(TimeSpan.FromMinutes(2)))
            .Should()
            .Be("Every 5 minutes, up to 2 minutes late");
        ScheduleText.Describe(Cron.Daily(hour: 2)).Should().Be("Daily at 02:00 UTC");
    }

    [Test]
    public void Describe_AManifestWithNoTime_SaysWhatStartsIt()
    {
        ScheduleText
            .Describe(new Manifest { ScheduleType = ScheduleType.Dependent }, "nightly-import")
            .Should()
            .Be("After nightly-import succeeds");
        ScheduleText
            .Describe(
                new Manifest { ScheduleType = ScheduleType.DormantDependent },
                "nightly-import"
            )
            .Should()
            .Be("When nightly-import activates it");
        ScheduleText
            .Describe(new Manifest { ScheduleType = ScheduleType.Dependent })
            .Should()
            .Be("After its parent succeeds");
        ScheduleText
            .Describe(new Manifest { ScheduleType = ScheduleType.OnDemand })
            .Should()
            .Be("On demand");
        ScheduleText
            .Describe(new Manifest { ScheduleType = ScheduleType.None })
            .Should()
            .Be("Only when triggered");
        ScheduleText
            .Describe(
                new Manifest
                {
                    ScheduleType = ScheduleType.Once,
                    ScheduledAt = new DateTime(2026, 10, 19, 2, 0, 0, DateTimeKind.Utc),
                }
            )
            .Should()
            .Be("Once at 2026-10-19 02:00 UTC");
    }
}
