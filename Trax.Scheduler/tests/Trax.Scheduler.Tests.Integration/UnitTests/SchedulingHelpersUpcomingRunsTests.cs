using AwesomeAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Trains.ManifestManager.Utilities;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// <see cref="SchedulingHelpers.UpcomingRuns"/> lists the times the manifest manager will find a
/// manifest due. The agreement tests poll <see cref="SchedulingHelpers.ShouldRunNow"/> once a
/// minute, the way the manager does, record each time it says yes as a successful run, and
/// expect the same list.
/// </summary>
[TestFixture]
public class SchedulingHelpersUpcomingRunsTests
{
    private static readonly DateTime Now = new(2026, 3, 2, 12, 0, 0, DateTimeKind.Utc);
    private readonly SchedulerConfiguration _config = new();

    private static IEnumerable<TestCaseData> AgreementCases()
    {
        yield return Case(
            "daily cron, a date excluded, catches up",
            Cron(
                "0 2 * * *",
                lastRun: Now.Date.AddHours(2),
                e: Exclude.Dates(new DateOnly(2026, 3, 3))
            )
        );
        yield return Case(
            "daily cron, a date excluded, does not catch up",
            Cron(
                "0 2 * * *",
                lastRun: Now.Date.AddHours(2),
                policy: MisfirePolicy.DoNothing,
                e: Exclude.Dates(new DateOnly(2026, 3, 3))
            )
        );
        yield return Case(
            "four-hourly cron inside a nightly window",
            Cron(
                "30 */4 * * *",
                lastRun: Now.AddMinutes(-30),
                e: Exclude.TimeWindow(new(22, 0), new(2, 0))
            )
        );
        yield return Case(
            "four-hourly cron inside a nightly window, does not catch up",
            Cron(
                "30 */4 * * *",
                lastRun: Now.AddMinutes(-30),
                policy: MisfirePolicy.DoNothing,
                e: Exclude.TimeWindow(new(22, 0), new(2, 0))
            )
        );
        yield return Case(
            "weekday cron, weekends excluded",
            Cron(
                "0 9 * * *",
                lastRun: Now.Date.AddHours(9),
                e: Exclude.DaysOfWeek(DayOfWeek.Saturday, DayOfWeek.Sunday)
            )
        );
        yield return Case(
            "interval, a range excluded",
            Interval(
                3600 * 6,
                lastRun: Now.AddMinutes(-10),
                e: Exclude.DateRange(new(2026, 3, 3), new(2026, 3, 4))
            )
        );
        yield return Case(
            "interval, a range excluded, does not catch up",
            Interval(
                3600 * 5,
                lastRun: Now.AddMinutes(-10),
                policy: MisfirePolicy.DoNothing,
                e: Exclude.DateRange(new(2026, 3, 3), new(2026, 3, 3))
            )
        );
        yield return Case("interval never run", Interval(300, lastRun: null));
        yield return Case(
            "cron overdue from a cancelled run",
            Cron("*/20 * * * *", lastRun: Now.AddHours(-5)),
            lastCancelled: Now.AddMinutes(-5)
        );
    }

    [TestCaseSource(nameof(AgreementCases))]
    public void UpcomingRuns_AreTheTimesAManagerPollingEveryMinuteFindsItDue(
        Manifest manifest,
        DateTime? lastCancelled
    )
    {
        var upcoming = SchedulingHelpers.UpcomingRuns(manifest, 6, Now, _config, lastCancelled);

        upcoming.Should().HaveCount(6).And.Equal(PollLikeTheManager(manifest, lastCancelled, 6));
    }

    [Test]
    public void UpcomingRuns_DoNotChangeTheManifest()
    {
        var lastRun = Now.AddHours(-1);
        var manifest = Interval(600, lastRun);

        SchedulingHelpers.UpcomingRuns(manifest, 5, Now, _config);

        manifest.LastSuccessfulRun.Should().Be(lastRun);
        manifest.NextScheduledRun.Should().BeNull();
    }

    [TestCase(ScheduleType.None)]
    [TestCase(ScheduleType.OnDemand)]
    [TestCase(ScheduleType.Dependent)]
    [TestCase(ScheduleType.DormantDependent)]
    public void UpcomingRuns_OfAScheduleWithNoTime_AreNone(ScheduleType type)
    {
        var manifest = Interval(60, lastRun: null);
        manifest.ScheduleType = type;

        SchedulingHelpers.UpcomingRuns(manifest, 5, Now, _config).Should().BeEmpty();
    }

    [Test]
    public void UpcomingRuns_OfADisabledManifest_AreNone()
    {
        var manifest = Interval(60, lastRun: null);
        manifest.IsEnabled = false;

        SchedulingHelpers.UpcomingRuns(manifest, 5, Now, _config).Should().BeEmpty();
    }

    [Test]
    public void UpcomingRuns_OfAOneOff_AreItsTimeOnly()
    {
        var manifest = new Manifest
        {
            ScheduleType = ScheduleType.Once,
            ScheduledAt = Now.AddHours(3),
            IsEnabled = true,
        };

        SchedulingHelpers.UpcomingRuns(manifest, 5, Now, _config).Should().Equal(Now.AddHours(3));
    }

    [Test]
    public void UpcomingRuns_OfACronWithARecordedFirstOccurrence_StartThere()
    {
        // A cron scheduled at noon for 03:00 records its first occurrence; it is not due at once.
        var manifest = Cron("0 3 * * *", lastRun: null);
        manifest.NextScheduledRun = Now.Date.AddDays(1).AddHours(3);

        SchedulingHelpers
            .UpcomingRuns(manifest, 2, Now, _config)
            .Should()
            .Equal(Now.Date.AddDays(1).AddHours(3), Now.Date.AddDays(2).AddHours(3));
    }

    [Test]
    public void UpcomingRuns_AreBoundedByTheMaximum()
    {
        SchedulingHelpers
            .UpcomingRuns(Interval(60, lastRun: Now), 500, Now, _config)
            .Should()
            .HaveCount(SchedulingHelpers.MaxUpcomingRuns);
    }

    [Test]
    public void UpcomingRuns_OfAScheduleEveryExclusionCovers_EndWithoutAnswer()
    {
        var manifest = Cron(
            "0 2 * * *",
            lastRun: Now,
            e: Exclude.DaysOfWeek(Enum.GetValues<DayOfWeek>())
        );

        SchedulingHelpers.UpcomingRuns(manifest, 3, Now, _config).Should().BeEmpty();
    }

    // ── Helpers ─────────────────────────────────────────────────────

    /// <summary>
    /// The manager's loop, compressed: evaluate due-ness once a minute and, when it is due, record
    /// a successful run at that minute.
    /// </summary>
    private List<DateTime> PollLikeTheManager(Manifest source, DateTime? lastCancelled, int count)
    {
        var manifest = new Manifest
        {
            ScheduleType = source.ScheduleType,
            CronExpression = source.CronExpression,
            IntervalSeconds = source.IntervalSeconds,
            MisfirePolicy = source.MisfirePolicy,
            Exclusions = source.Exclusions,
            LastSuccessfulRun = source.LastSuccessfulRun,
            NextScheduledRun = source.NextScheduledRun,
            IsEnabled = true,
        };
        var runs = new List<DateTime>();
        for (var t = Now; runs.Count < count && t < Now.AddDays(30); t = t.AddMinutes(1))
        {
            if (
                !SchedulingHelpers.ShouldRunNow(
                    manifest,
                    t,
                    _config,
                    NullLogger.Instance,
                    lastCancelled
                )
            )
                continue;
            runs.Add(t);
            manifest.LastSuccessfulRun = t;
            manifest.NextScheduledRun = null;
            lastCancelled = null;
        }
        return runs;
    }

    private static TestCaseData Case(
        string name,
        Manifest manifest,
        DateTime? lastCancelled = null
    ) => new TestCaseData(manifest, lastCancelled).SetArgDisplayNames(name);

    private static Manifest Cron(
        string expression,
        DateTime? lastRun,
        MisfirePolicy policy = MisfirePolicy.FireOnceNow,
        params Exclusion[] e
    )
    {
        var manifest = new Manifest
        {
            ScheduleType = ScheduleType.Cron,
            CronExpression = expression,
            LastSuccessfulRun = lastRun,
            MisfirePolicy = policy,
            IsEnabled = true,
        };
        manifest.SetExclusions([.. e]);
        return manifest;
    }

    private static Manifest Interval(
        int seconds,
        DateTime? lastRun,
        MisfirePolicy policy = MisfirePolicy.FireOnceNow,
        params Exclusion[] e
    )
    {
        var manifest = new Manifest
        {
            ScheduleType = ScheduleType.Interval,
            IntervalSeconds = seconds,
            LastSuccessfulRun = lastRun,
            MisfirePolicy = policy,
            IsEnabled = true,
        };
        manifest.SetExclusions([.. e]);
        return manifest;
    }
}
