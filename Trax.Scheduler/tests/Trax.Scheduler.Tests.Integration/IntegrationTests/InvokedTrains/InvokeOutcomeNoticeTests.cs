using AwesomeAssertions;
using Trax.Effect.StateMachine.Persistence;
using Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests.InvokedTrains;

/// <summary>
/// On Postgres, an invoked run ending on one host wakes the outcome reconciler on another at once, through the
/// notification migration 073's triggers send when the run's terminal write commits, rather than at its next
/// sweep. Postgres only: SQLite has no notification channel. See
/// <c>Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md</c>.
/// </summary>
[TestFixture]
[NonParallelizable]
public class InvokeOutcomeNoticeTests
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    private InvokeCluster _cluster = null!;
    private ClusterHost _api = null!;
    private ClusterHost _worker = null!;

    [OneTimeSetUp]
    public async Task CreateCluster()
    {
        _cluster = await InvokeCluster.Create(ClusterStore.Postgres);

        // A sweep interval no test waits out: only the notice can deliver within the bound.
        _api = _cluster.Host(
            machines: true,
            scheduler: false,
            options: o => o.InvokeOutcomeSweepInterval = TimeSpan.FromHours(1)
        );
        _worker = _cluster.Host(machines: false, scheduler: true);
        await _cluster.Reset();
    }

    [OneTimeTearDown]
    public async Task DisposeCluster()
    {
        await _api.DisposeAsync();
        await _worker.DisposeAsync();
        await _cluster.DisposeAsync();
    }

    [Test]
    public async Task A_run_ending_on_another_host_wakes_the_reconciler_before_its_interval()
    {
        var reconciler = _api.Reconciler;
        await reconciler.StartAsync(CancellationToken.None);
        try
        {
            await Task.WhenAll(reconciler.Listening, reconciler.FirstSweep).WaitAsync(Bound);

            var instance = await _api.Start(StepMachine.Context(InvokedStepModes.Ok));
            var token = (await _api.Row(instance.Id, SystemStepMachine.MachineId))!.InvokeToken!;
            var delivered = new TaskCompletionSource<InvokeDelivery>(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            reconciler.Delivered += (run, result) =>
            {
                if (run == token)
                    delivered.TrySetResult(result);
            };

            await _worker.DispatchAndRun(token);

            (await delivered.Task.WaitAsync(Bound))
                .Should()
                .BeEquivalentTo(
                    new { To = "Done" },
                    "the run's terminal write notified every host, and this one delivered it"
                );
        }
        finally
        {
            await reconciler.StopAsync(CancellationToken.None);
        }
    }
}
