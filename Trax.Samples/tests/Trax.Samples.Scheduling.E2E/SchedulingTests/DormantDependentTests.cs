using System.Text.Json;
using Trax.Effect.Enums;
using Trax.Samples.Scheduling.E2E.Fixtures;
using Trax.Samples.Shared.Testing;

namespace Trax.Samples.Scheduling.E2E.SchedulingTests;

/// <summary>
/// A dormant dependent runs only when its parent activates it through
/// <c>IDormantDependentContext</c>, with the input the parent hands it.
/// </summary>
public class DormantDependentTests : SchedulingTestFixture
{
    [TearDown]
    public void RestoreFeed() => RateFeed.SpikesEnabled = true;

    [Test]
    public async Task Parent_activates_the_dormant_dependent_with_its_own_input()
    {
        var alert = await Db.Manifest(ManifestNames.AlertRateSpike);
        var marker = await Db.LatestMetadataId();

        RateFeed.SpikeNext();

        var run = await Db.WaitForRun(
            alert.Id,
            TrainState.Completed,
            marker,
            TimeSpan.FromSeconds(30)
        );

        run.Input.Should().NotBeNull("the host saves train parameters");
        using var input = JsonDocument.Parse(run.Input!);
        input.RootElement.GetProperty("changePercent").GetDecimal().Should().Be(7.5m);
    }

    [Test]
    public async Task Parent_success_alone_does_not_fire_the_dormant_dependent()
    {
        var refresh = await Db.Manifest(ManifestNames.RefreshExchangeRates);
        var reprice = await Db.Manifest(ManifestNames.RepriceCatalog);
        var alert = await Db.Manifest(ManifestNames.AlertRateSpike);

        RateFeed.SpikesEnabled = false;
        // A spike activated before the feed went calm may still be on its way.
        await Db.WaitUntil(
            async () =>
                (await Db.Runs(alert.Id, 0)).All(r =>
                    r.TrainState is not (TrainState.Pending or TrainState.InProgress)
                ),
            TimeSpan.FromSeconds(15),
            "no spike alert is in flight"
        );
        var marker = await Db.LatestMetadataId();

        // Two parent successes, each followed by its ordinary dependent.
        var parentRuns = await Db.WaitForRuns(
            refresh.Id,
            TrainState.Completed,
            2,
            marker,
            TimeSpan.FromSeconds(30)
        );
        await Db.WaitForRun(
            reprice.Id,
            TrainState.Completed,
            parentRuns[1].Id,
            TimeSpan.FromSeconds(20)
        );

        (await Db.Runs(alert.Id, marker)).Should().BeEmpty();
    }
}
