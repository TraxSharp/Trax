using Trax.Effect.Enums;
using Trax.Samples.Scheduling.E2E.Fixtures;

namespace Trax.Samples.Scheduling.E2E.SchedulingTests;

public class DependentManifestTests : SchedulingTestFixture
{
    [Test]
    public async Task Dependent_runs_after_its_parent_succeeds()
    {
        var refresh = await Db.Manifest(ManifestNames.RefreshExchangeRates);
        var reprice = await Db.Manifest(ManifestNames.RepriceCatalog);
        var marker = await Db.LatestMetadataId();

        var parentRun = await Db.WaitForRun(
            refresh.Id,
            TrainState.Completed,
            marker,
            TimeSpan.FromSeconds(20)
        );
        var dependentRun = await Db.WaitForRun(
            reprice.Id,
            TrainState.Completed,
            parentRun.Id,
            TimeSpan.FromSeconds(20)
        );

        dependentRun.StartTime.Should().BeOnOrAfter(parentRun.EndTime!.Value);
    }

    [Test]
    public async Task Dependent_does_not_run_while_its_parent_is_disabled()
    {
        var refresh = await Db.Manifest(ManifestNames.RefreshExchangeRates);
        var reprice = await Db.Manifest(ManifestNames.RepriceCatalog);

        await Scheduler.DisableAsync(ManifestNames.RefreshExchangeRates);
        try
        {
            // Let whatever was already dispatched finish, then mark.
            await Db.WaitUntil(
                async () =>
                    (await Db.Runs(refresh.Id, 0))
                        .Concat(await Db.Runs(reprice.Id, 0))
                        .All(r =>
                            r.TrainState is not (TrainState.Pending or TrainState.InProgress)
                        ),
                TimeSpan.FromSeconds(15),
                "in-flight runs finish after the parent is disabled"
            );
            var marker = await Db.LatestMetadataId();

            var ran = await Samples.Shared.Testing.Polling.WaitUntilAsync(
                async () =>
                    (await Db.Runs(refresh.Id, marker)).Count > 0
                    || (await Db.Runs(reprice.Id, marker)).Count > 1,
                TimeSpan.FromSeconds(8),
                TimeSpan.FromMilliseconds(250)
            );
            ran.Should().BeFalse("a disabled parent neither runs nor fires its dependent");
        }
        finally
        {
            await Scheduler.EnableAsync(ManifestNames.RefreshExchangeRates);
        }
    }
}
