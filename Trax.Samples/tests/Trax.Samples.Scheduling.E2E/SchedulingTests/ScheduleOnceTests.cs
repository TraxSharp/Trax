using Trax.Effect.Enums;
using Trax.Samples.Scheduling.E2E.Fixtures;
using Trax.Samples.Shared.Testing;

namespace Trax.Samples.Scheduling.E2E.SchedulingTests;

/// <summary>
/// <c>ScheduleOnce</c>: the manifest runs once its delay has passed and disables itself after
/// that first success.
/// </summary>
public class ScheduleOnceTests : SchedulingTestFixture
{
    [Test]
    public async Task One_off_manifest_runs_once_after_its_delay_then_disables_itself()
    {
        var seeded = await Db.Manifest(ManifestNames.SendLaunchAnnouncement);

        var run = await Db.WaitForRun(
            seeded.Id,
            TrainState.Completed,
            afterId: 0,
            TimeSpan.FromSeconds(40)
        );
        run.StartTime.Should().BeOnOrAfter(seeded.ScheduledAt!.Value);

        await Db.WaitUntil(
            async () => !(await Db.Manifest(ManifestNames.SendLaunchAnnouncement)).IsEnabled,
            TimeSpan.FromSeconds(10),
            "a once manifest disables itself after its first success"
        );

        // Several manifest-manager cycles later, still the one run.
        var ranAgain = await Polling.WaitUntilAsync(
            async () => (await Db.Runs(seeded.Id, afterId: 0)).Count > 1,
            TimeSpan.FromSeconds(4),
            TimeSpan.FromMilliseconds(250)
        );
        ranAgain.Should().BeFalse("a disabled once manifest is not queued again");
    }
}
