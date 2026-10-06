using Trax.Samples.Recovery.E2E.Fixtures;
using Trax.Samples.Shared.Testing;

namespace Trax.Samples.Recovery.E2E.RecoveryTests;

/// <summary>
/// The scheduler records a run of its own every time it polls. The host sweeps those, so leaving
/// the demo running does not fill the database, and keeps the scenario trains' runs.
/// </summary>
[TestFixture]
public class MetadataCleanupTests : RecoveryTestFixture
{
    private const string ManifestManager =
        "Trax.Scheduler.Trains.ManifestManager.IManifestManagerTrain";

    [Test]
    public async Task TheSchedulersOwnRuns_AreSweptAfterTheirRetention()
    {
        // The scheduler has been recording runs since the host started, before this test did.
        // The factory keeps them three seconds and sweeps every second, so soon every run left
        // started after this test: the ones from before were swept. Without the sweep, the
        // oldest run stays older than the test forever.
        var testStarted = DateTime.UtcNow;

        var swept = await Polling.WaitUntilAsync(
            async () =>
            {
                var response = await GraphQL.SendAsync(
                    $$"""{ operations { executions(trainName: "{{ManifestManager}}", order: OLDEST, take: 1) { items { startTime } } } }""",
                    OperatorKey
                );
                if (response.HasErrors)
                    return false;
                var items = response.GetData("operations", "executions", "items");
                return items.GetArrayLength() > 0
                    && items[0].GetProperty("startTime").GetDateTime().ToUniversalTime()
                        > testStarted;
            },
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMilliseconds(500)
        );

        swept.Should().BeTrue("the host sweeps the scheduler's own runs after their retention");
    }
}
