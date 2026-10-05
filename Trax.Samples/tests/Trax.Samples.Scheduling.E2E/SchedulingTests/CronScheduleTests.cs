using Trax.Samples.Scheduling.E2E.Fixtures;

namespace Trax.Samples.Scheduling.E2E.SchedulingTests;

public class CronScheduleTests : SchedulingTestFixture
{
    [Test]
    public async Task New_cron_manifest_waits_for_its_next_occurrence_in_utc()
    {
        var digest = await Db.Manifest(ManifestNames.SendDailyDigest);
        var runs = await Db.Runs(digest.Id, afterId: 0);
        var now = DateTime.UtcNow;

        // The host seeded the manifest just after the suite started.
        var startedAt = SharedSchedulingSetup.StartedAt;
        var first07 = startedAt.Date.AddHours(7);
        if (first07 <= startedAt)
            first07 = first07.AddDays(1);

        digest.NextScheduledRun.Should().NotBeNull();
        var next = digest.NextScheduledRun!.Value;
        next.TimeOfDay.Should().Be(TimeSpan.FromHours(7), "the cron is 07:00 UTC");

        if (first07 > now)
        {
            next.Should().Be(first07);
            runs.Should().BeEmpty("a new cron manifest does not run at startup");
        }
        else
        {
            // 07:00 UTC passed while the suite ran: the manager runs the digest then and moves
            // the next run on to a later 07:00 (or has not got to it yet), never past the one
            // after now.
            next.Should().BeOnOrAfter(first07).And.BeOnOrBefore(now.Date.AddDays(1).AddHours(7));
        }
    }
}
