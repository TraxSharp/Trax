using Trax.Effect.Enums;
using Trax.Samples.Scheduling.E2E.Fixtures;

namespace Trax.Samples.Scheduling.E2E.SchedulingTests;

public class IntervalScheduleTests : SchedulingTestFixture
{
    [Test]
    public async Task Interval_manifest_runs_again_once_its_interval_has_passed()
    {
        var refresh = await Db.Manifest(ManifestNames.RefreshExchangeRates);
        var marker = await Db.LatestMetadataId();

        var runs = await Db.WaitForRuns(
            refresh.Id,
            TrainState.Completed,
            3,
            marker,
            TimeSpan.FromSeconds(40)
        );

        // An interval counts from the last success, so consecutive runs start at least the
        // interval apart, and with one-second polling not much more.
        for (var i = 1; i < runs.Count; i++)
        {
            var gap = runs[i].StartTime - runs[i - 1].StartTime;
            gap.Should().BeGreaterThan(TimeSpan.FromSeconds(4.5));
            gap.Should().BeLessThan(TimeSpan.FromSeconds(12));
        }
    }
}
