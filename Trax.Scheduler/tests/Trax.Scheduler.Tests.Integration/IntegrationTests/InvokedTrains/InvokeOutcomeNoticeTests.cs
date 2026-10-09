using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Trax.Core.Functional;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.StateMachine.Persistence;
using Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.JobDispatcher;

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
            var delivered = DeliveryOf(reconciler, token);

            await _worker.DispatchAndRun(token);

            (await delivered.WaitAsync(Bound))
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

    [Test]
    public async Task A_run_recorded_already_failed_wakes_the_reconciler()
    {
        await using var api = ListeningHost();
        var reconciler = api.Reconciler;
        await reconciler.StartAsync(CancellationToken.None);
        try
        {
            await Task.WhenAll(reconciler.Listening, reconciler.FirstSweep).WaitAsync(Bound);

            var instance = await api.Start(StepMachine.Context(InvokedStepModes.Ok));
            var token = (await api.Row(instance.Id, SystemStepMachine.MachineId))!.InvokeToken!;
            var delivered = DeliveryOf(reconciler, token);

            // An input that no longer reads as its train's: the dispatcher inserts the run already failed, and
            // never updates its state.
            using (var scope = api.Services.CreateScope())
                await scope
                    .ServiceProvider.GetRequiredService<IDataContext>()
                    .WorkQueues.Where(w => w.ExternalId == token)
                    .ExecuteUpdateAsync(s => s.SetProperty(w => w.Input, "[1]"));
            using (var scope = _worker.Services.CreateScope())
                await scope
                    .ServiceProvider.GetRequiredService<IJobDispatcherTrain>()
                    .Run(Unit.Default);
            var run = (await _worker.Entry(token))!.MetadataId!.Value;
            (await _worker.Run(run)).TrainState.Should().Be(TrainState.Failed);

            (await delivered.WaitAsync(Bound))
                .Should()
                .BeEquivalentTo(
                    new { To = "Failed", Applied = "failed" },
                    "a run inserted already failed notifies as one that is updated to failed does"
                );
        }
        finally
        {
            await reconciler.StopAsync(CancellationToken.None);
        }
    }

    [Test]
    public async Task The_reconciler_subscribes_again_after_its_connection_is_terminated_and_sweeps_in_full()
    {
        await using var api = ListeningHost();
        var reconciler = api.Reconciler;
        var subscribed = new SignalCount();
        var swept = new SignalCount();
        reconciler.Subscribed += subscribed.Increment;
        reconciler.Sweeping += swept.Increment;
        await reconciler.StartAsync(CancellationToken.None);
        try
        {
            await Task.WhenAll(reconciler.Listening, reconciler.FirstSweep).WaitAsync(Bound);
            swept.Count.Should().Be(1, "the interval is an hour: only the start has swept");

            await using (var admin = new NpgsqlConnection(_cluster.ConnectionString))
            {
                await admin.OpenAsync();
                await using var terminate = new NpgsqlCommand(
                    "SELECT count(pg_terminate_backend(pid)) FROM pg_stat_activity "
                        + "WHERE application_name = 'trax_invoked_run_listener' AND datname = current_database()",
                    admin
                );
                ((long)(await terminate.ExecuteScalarAsync())!)
                    .Should()
                    .Be(1, "the host holds one listening session");
            }

            // The first retry comes after a one-second back-off, and asks for a full sweep: what ended while it
            // was not listening was never notified.
            await subscribed.Reached(2).WaitAsync(Bound);
            await swept.Reached(2).WaitAsync(Bound);

            // And it hears notices again.
            var instance = await api.Start(StepMachine.Context(InvokedStepModes.Ok));
            var token = (await api.Row(instance.Id, SystemStepMachine.MachineId))!.InvokeToken!;
            var delivered = DeliveryOf(reconciler, token);
            await _worker.DispatchAndRun(token);
            (await delivered.WaitAsync(Bound)).Should().BeEquivalentTo(new { To = "Done" });
        }
        finally
        {
            await reconciler.StopAsync(CancellationToken.None);
        }
    }

    // A host of its own, so its reconciler starts afresh; it sweeps only when asked to.
    private ClusterHost ListeningHost() =>
        _cluster.Host(
            machines: true,
            scheduler: false,
            options: o => o.InvokeOutcomeSweepInterval = TimeSpan.FromHours(1)
        );

    private static Task<InvokeDelivery> DeliveryOf(InvokeOutcomeReconciler reconciler, string token)
    {
        var delivered = new TaskCompletionSource<InvokeDelivery>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        reconciler.Delivered += (run, result) =>
        {
            if (run == token)
                delivered.TrySetResult(result);
        };
        return delivered.Task;
    }

    // Counts a signal, and completes a wait once it has been raised a number of times.
    private sealed class SignalCount
    {
        private readonly Lock _gate = new();
        private readonly List<(int Target, TaskCompletionSource Done)> _waits = [];
        private int _count;

        public int Count
        {
            get
            {
                lock (_gate)
                    return _count;
            }
        }

        public void Increment()
        {
            lock (_gate)
            {
                _count++;
                foreach (var wait in _waits.Where(w => w.Target <= _count))
                    wait.Done.TrySetResult();
            }
        }

        public Task Reached(int target)
        {
            lock (_gate)
            {
                var done = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                if (_count >= target)
                    done.TrySetResult();
                else
                    _waits.Add((target, done));
                return done.Task;
            }
        }
    }
}
