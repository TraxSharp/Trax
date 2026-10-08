using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.SnapshotDraft;
using Trax.Effect.StateMachine.Persistence;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.MetadataCleanup;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests.InvokedTrains;

/// <summary>
/// An invoked run's outcome comes back to the one entry of the state that queued it, exactly once, by a
/// conditional update on its token, whichever host delivers it and whatever crashed in between. The run's own
/// host delivers it through a lifecycle hook when it registers the machine; every host that registers machines
/// sweeps for the rest. Each test drives the dispatcher, the job runner, the manifest manager and the sweep itself,
/// so every wait is on a step it started, bounded by <see cref="Bound"/>. See
/// <c>Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md</c>.
/// </summary>
[TestFixture(ClusterStore.Postgres)]
[TestFixture(ClusterStore.Sqlite)]
[NonParallelizable]
public class InvokeOutcomeDeliveryTests(ClusterStore store)
{
    private const string Adr =
        "Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md";

    // Every wait in this class: long past any step here, short enough that a hang fails the test.
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    private InvokeCluster _cluster = null!;

    // Registers the machines: their hook and their reconciler. No scheduler.
    private ClusterHost _api = null!;

    // A second API host over the same database.
    private ClusterHost _api2 = null!;

    // Dispatches and runs trains, and registers no machines, so nothing on it delivers an outcome: a run it
    // finishes is a run whose host died after its terminal write.
    private ClusterHost _worker = null!;

    // Dispatches and runs trains and registers the machines, so its hook delivers each outcome at once.
    private ClusterHost _full = null!;

    [OneTimeSetUp]
    public async Task CreateCluster()
    {
        _cluster = await InvokeCluster.Create(store);
        _api = _cluster.Host(machines: true, scheduler: false);
        _api2 = _cluster.Host(machines: true, scheduler: false);
        _worker = _cluster.Host(machines: false, scheduler: true);
        _full = _cluster.Host(machines: true, scheduler: true);
    }

    [OneTimeTearDown]
    public async Task DisposeCluster()
    {
        await _api.DisposeAsync();
        await _api2.DisposeAsync();
        await _worker.DisposeAsync();
        await _full.DisposeAsync();
        await _cluster.DisposeAsync();
    }

    [SetUp]
    public async Task Reset()
    {
        InvokedStepGate.Reset();
        FaultingLauncher.Armed = false;
        await _cluster.Reset();
    }

    [TearDown]
    public void ReleaseGate() => InvokedStepGate.Release();

    [Test]
    public async Task A_crash_after_the_run_finishes_and_before_the_hook_is_applied_once_by_the_reconciler()
    {
        var instance = await _api.Start(StepMachine.Context(InvokedStepModes.Ok, note: "crash"));
        var token = (await SystemRow(instance.Id)).InvokeToken!;

        // The run's terminal write commits on a host that delivers nothing, as if it died right after.
        var runId = await _worker.DispatchAndRun(token);
        (await _worker.Run(runId)).TrainState.Should().Be(TrainState.Completed);
        (await SystemRow(instance.Id))
            .State.Should()
            .Be("Running", "nothing has delivered the outcome yet");

        var first = await _api.Sweep();

        first
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(
                new { To = "Done", Applied = "done" },
                $"the sweep applies a finished run's outcome its hook never delivered. See {Adr}"
            );
        var row = await SystemRow(instance.Id);
        row.State.Should().Be("Done");
        row.InvokeToken.Should().BeNull();
        Context(row)["artifact"]!
            .GetValue<string>()
            .Should()
            .Be(
                "artifact:crash",
                "OnDone reduces the run's own output, recorded in its terminal write"
            );

        (await _api.Sweep()).Should().BeEmpty("the outcome is applied once");
        (await _api.Deliver(token)).Should().BeOfType<InvokeDelivery.NoTransition>();
    }

    [Test]
    public async Task Two_hosts_delivering_one_completion_apply_it_once()
    {
        // A chained outcome, so applying it twice would also queue its next run twice.
        var instance = await _api.Start(StepMachine.Context(InvokedStepModes.Chain));
        var token = (await SystemRow(instance.Id)).InvokeToken!;
        await _worker.DispatchAndRun(token);

        var sweeps = Task.WhenAll(_api.Sweep(), _api2.Sweep());
        var deliveries = Task.WhenAll(_api.Deliver(token), _api2.Deliver(token));
        await Task.WhenAll(sweeps, deliveries).WaitAsync(Bound);

        var all = sweeps.Result.SelectMany(r => r).Concat(deliveries.Result).ToList();
        all.OfType<InvokeDelivery.Moved>()
            .Should()
            .ContainSingle(
                $"one conditional update on the token matches, whoever sends it. See {Adr}"
            );
        all.Should()
            .AllSatisfy(r =>
                r.Should().Match(x => x is InvokeDelivery.Moved || x is InvokeDelivery.NoTransition)
            );

        var row = await SystemRow(instance.Id);
        row.State.Should().Be("Chained");
        var entries = await _api.Entries(instance.Id);
        entries.Should().HaveCount(2, "the chained state queued its next run once");
        row.InvokeToken.Should().Be(entries[1].ExternalId);
    }

    [Test]
    public async Task A_completion_after_the_state_was_left_is_a_no_transition()
    {
        var user = NewUser();
        var id = Guid.NewGuid();
        (await _api.EnterRunning(user, id, StepMachine.Context(InvokedStepModes.Ok)))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();
        var token = (await UserRow(id)).InvokeToken!;
        await _worker.DispatchAndRun(token);

        // The user leaves before the completion is delivered.
        (await _api.Advance(user, id, StepTrigger.Stop))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();

        (await _api.Sweep()).Should().BeEmpty();
        (await _api.Deliver(token))
            .Should()
            .BeOfType<InvokeDelivery.NoTransition>(
                $"the token was cleared when the state was left, so the completion has nowhere to land. See {Adr}"
            );
        var row = await UserRow(id);
        row.State.Should().Be("Idle");
        Context(row)["artifact"]!.GetValue<string>().Should().BeEmpty();
    }

    [Test]
    public async Task A_run_failed_by_the_reaper_reaches_OnFailed()
    {
        var swept = await _api.Start(StepMachine.Context(InvokedStepModes.Ok));
        var sweptToken = (await SystemRow(swept.Id)).InvokeToken!;
        var sweptRun = await _worker.Dispatch(sweptToken);

        // Dispatched and never picked up; the reaper fails it, and nothing on its host delivers the outcome.
        await _worker.Age(sweptRun, TimeSpan.FromHours(1));
        await _worker.RunManifestManager();
        (await _worker.Run(sweptRun)).TrainState.Should().Be(TrainState.Failed);
        (await SystemRow(swept.Id)).State.Should().Be("Running");

        (await _api.Sweep())
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(
                new { To = "Failed", Applied = "failed" },
                $"a reaped run reaches OnFailed through the sweep. See {Adr}"
            );
        (await SystemRow(swept.Id)).State.Should().Be("Failed");

        // On a host that registers the machine, the reaper's own lifecycle event delivers it at once.
        var hooked = await _api.Start(StepMachine.Context(InvokedStepModes.Ok));
        var hookedRun = await _full.Dispatch((await SystemRow(hooked.Id)).InvokeToken!);
        await _full.Age(hookedRun, TimeSpan.FromHours(1));
        await _full.RunManifestManager();

        (await SystemRow(hooked.Id))
            .State.Should()
            .Be("Failed", "the reaper publishes the run's failure, and the hook applies it");
    }

    [Test]
    public async Task A_timed_out_run_and_an_operator_cancel_reach_OnCancelled()
    {
        // Timed out: the manifest manager flags a run past its timeout, and it stops at its next junction.
        var timedOut = await _api.Start(StepMachine.Context(InvokedStepModes.Gate));
        var timedOutRun = await _worker.Dispatch((await SystemRow(timedOut.Id)).InvokeToken!);
        var job = _worker.RunJob(timedOutRun);
        await InvokedStepGate.Entered.WaitAsync(Bound);
        await _worker.Age(timedOutRun, TimeSpan.FromMinutes(30));
        await _worker.RunManifestManager();
        InvokedStepGate.Release();
        await Settled(job);

        (await _worker.Run(timedOutRun)).TrainState.Should().Be(TrainState.Cancelled);
        await SweepExpecting(timedOut.Id, "Cancelled");

        // An operator's cancel of a running run.
        InvokedStepGate.Reset();
        var cancelled = await _api.Start(StepMachine.Context(InvokedStepModes.Gate));
        var cancelledRun = await _worker.Dispatch((await SystemRow(cancelled.Id)).InvokeToken!);
        job = _worker.RunJob(cancelledRun);
        await InvokedStepGate.Entered.WaitAsync(Bound);
        using (var scope = _worker.Services.CreateScope())
            (
                await scope
                    .ServiceProvider.GetRequiredService<IOperationsService>()
                    .CancelExecutionsAsync([cancelledRun], CancellationToken.None)
            )
                .Success.Should()
                .BeTrue();
        InvokedStepGate.Release();
        await Settled(job);

        (await _worker.Run(cancelledRun)).TrainState.Should().Be(TrainState.Cancelled);
        await SweepExpecting(cancelled.Id, "Cancelled");

        // An operator's cancel of a run still only queued: there is no run, and the entry says so.
        var queued = await _api.Start(StepMachine.Context(InvokedStepModes.Ok));
        var entry = await _api.Entry((await SystemRow(queued.Id)).InvokeToken!);
        using (var scope = _worker.Services.CreateScope())
            (
                await scope
                    .ServiceProvider.GetRequiredService<IOperationsService>()
                    .CancelWorkQueueEntryAsync(entry!.Id, CancellationToken.None)
            )
                .Success.Should()
                .BeTrue();

        await SweepExpecting(queued.Id, "Cancelled");
    }

    [Test]
    public async Task Reentering_after_OnFailed_queues_a_new_run_and_the_old_completion_is_a_no_transition()
    {
        var user = NewUser();
        var id = Guid.NewGuid();
        await _api.EnterRunning(user, id, StepMachine.Context(InvokedStepModes.Fail));
        var first = (await UserRow(id)).InvokeToken!;
        await _worker.DispatchAndRun(first);
        (await _api.Sweep())
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(new { To = "Failed" });

        (await _api.Advance(user, id, StepTrigger.Retry))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();
        var row = await UserRow(id);
        row.State.Should().Be("Running");
        var second = row.InvokeToken!;
        second.Should().NotBe(first, "entering the state again mints a new run and a new token");
        (await _api.Entries(id)).Should().HaveCount(2);

        (await _api.Deliver(first))
            .Should()
            .BeOfType<InvokeDelivery.NoTransition>(
                $"the old run's late completion names a token the row no longer holds. See {Adr}"
            );
        (await UserRow(id)).InvokeToken.Should().Be(second);
    }

    [Test]
    public async Task An_outcome_past_64_KiB_goes_to_OnFailed_with_its_reason()
    {
        // An output past the cap is never stored, only marked.
        var big = await _api.Start(StepMachine.Context(InvokedStepModes.Big));
        var bigRun = await _worker.DispatchAndRun((await SystemRow(big.Id)).InvokeToken!);
        (await _worker.Run(bigRun)).TrainState.Should().Be(TrainState.Completed);

        (await _api.Sweep())
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(
                new
                {
                    To = "Failed",
                    Applied = "failed",
                    Reason = InvokeOutcomeReasons.TooLarge,
                },
                $"an outcome too large to store fails the state with its reason, never dropped. See {Adr}"
            );
        (await SystemRow(big.Id)).State.Should().Be("Failed");

        // An output under the cap whose reduction would take the snapshot past it.
        var grow = await _api.Start(
            StepMachine.Context(InvokedStepModes.Grow, padBytes: 30 * 1024)
        );
        await _worker.DispatchAndRun((await SystemRow(grow.Id)).InvokeToken!);

        (await _api.Sweep())
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(
                new
                {
                    To = "Failed",
                    Applied = "failed",
                    Reason = InvokeOutcomeReasons.TooLarge,
                }
            );
        var row = await SystemRow(grow.Id);
        row.State.Should().Be("Failed");
        Context(row)["artifact"]!
            .GetValue<string>()
            .Should()
            .BeEmpty("the reduction was not applied");
    }

    [Test]
    public async Task Leaving_the_invoking_state_cancels_the_run_on_another_host()
    {
        var user = NewUser();
        var id = Guid.NewGuid();
        await _api.EnterRunning(user, id, StepMachine.Context(InvokedStepModes.Gate));
        var token = (await UserRow(id)).InvokeToken!;
        var runId = await _worker.Dispatch(token);
        var job = _worker.RunJob(runId);
        await InvokedStepGate.Entered.WaitAsync(Bound);

        // The API host leaves the state while the worker runs the train: it can reach the run only through
        // the database.
        (await _api.Advance(user, id, StepTrigger.Stop))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();
        (await _worker.Run(runId)).CancellationRequested.Should().BeTrue();
        InvokedStepGate.Release();
        await Settled(job);

        (await _worker.Run(runId))
            .TrainState.Should()
            .Be(
                TrainState.Cancelled,
                $"the run reads its cancel flag at its next junction, on its own host. See {Adr}"
            );
        (await _api.Sweep()).Should().BeEmpty();
        (await _api.Deliver(token))
            .Should()
            .BeOfType<InvokeDelivery.NoTransition>(
                "the cancel that leaving caused is not an outcome: the state was left"
            );
        (await UserRow(id)).State.Should().Be("Idle");
    }

    [Test]
    public async Task An_outcome_whose_target_invokes_queues_the_next_run_in_the_same_transaction()
    {
        await using var faulty = _cluster.Host(
            machines: true,
            scheduler: false,
            FaultingLauncher.Install
        );
        var instance = await _api.Start(StepMachine.Context(InvokedStepModes.Chain));
        var token = (await SystemRow(instance.Id)).InvokeToken!;
        await _worker.DispatchAndRun(token);

        // The host dies after the next run's entry is written and before the outcome commits: neither half is.
        FaultingLauncher.Armed = true;
        (await faulty.Deliver(token)).Should().BeNull("the delivery failed");
        var row = await SystemRow(instance.Id);
        row.State.Should().Be("Running");
        row.InvokeToken.Should().Be(token);
        (await _api.Entries(instance.Id))
            .Should()
            .ContainSingle("the next run's entry rolled back");

        FaultingLauncher.Armed = false;
        (await _api.Sweep())
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeOfType<InvokeDelivery.Moved>()
            .Which.NextToken.Should()
            .NotBeNull();
        row = await SystemRow(instance.Id);
        row.State.Should().Be("Chained");
        var entries = await _api.Entries(instance.Id);
        entries.Should().HaveCount(2);
        row.InvokeToken.Should().Be(entries[1].ExternalId);

        // The chained run's own outcome comes back the same way, through the hook on a host with the machine.
        await _full.DispatchAndRun(row.InvokeToken!);
        (await SystemRow(instance.Id)).State.Should().Be("Done");
    }

    [Test]
    public async Task A_completed_run_whose_output_no_OnDone_accepts_goes_to_OnFailed()
    {
        var instance = await _api.Start(StepMachine.Context(InvokedStepModes.Unaccepted));
        await _worker.DispatchAndRun((await SystemRow(instance.Id)).InvokeToken!);

        (await _api.Sweep())
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(
                new
                {
                    To = "Failed",
                    Applied = "failed",
                    Reason = InvokeOutcomeReasons.OutputUnaccepted,
                },
                $"a finished run never leaves its instance waiting: unaccepted output is a failure. See {Adr}"
            );
        (await SystemRow(instance.Id)).InvokeToken.Should().BeNull();
    }

    [Test]
    public async Task A_run_whose_records_were_deleted_reaches_OnFailed()
    {
        // A finished run whose outcome no host delivered, then deleted with its entry, its logs and all.
        var finished = await _api.Start(StepMachine.Context(InvokedStepModes.Ok));
        var finishedToken = (await SystemRow(finished.Id)).InvokeToken!;
        var runId = await _worker.DispatchAndRun(finishedToken);
        await DeleteRun(runId);

        // A run deleted while still queued.
        var queued = await _api.Start(StepMachine.Context(InvokedStepModes.Ok));
        var queuedToken = (await SystemRow(queued.Id)).InvokeToken!;
        await DeleteEntry(queuedToken);

        var swept = await _api.Sweep();

        swept
            .Should()
            .HaveCount(2)
            .And.AllSatisfy(d =>
                d.Should()
                    .BeEquivalentTo(
                        new
                        {
                            To = "Failed",
                            Applied = "failed",
                            Reason = InvokeOutcomeReasons.RunMissing,
                        },
                        $"a run that can no longer be found can never end, so the instance does not wait on "
                            + $"it. See {Adr}"
                    )
            );
        foreach (var id in new[] { finished.Id, queued.Id })
        {
            var row = await SystemRow(id);
            row.State.Should().Be("Failed");
            row.InvokeToken.Should().BeNull();
        }
        (await _api.Deliver(finishedToken)).Should().BeOfType<InvokeDelivery.NoTransition>();
    }

    [Test]
    public async Task Metadata_cleanup_keeps_an_invoked_run_while_its_token_is_live()
    {
        await using var cleaner = _cluster.Host(
            machines: false,
            scheduler: true,
            scheduling: s =>
                s.AddMetadataCleanup(c => c.AddTrainType(typeof(IInvokedStepTrain).FullName!))
        );
        var instance = await _api.Start(StepMachine.Context(InvokedStepModes.Ok, note: "kept"));
        var token = (await SystemRow(instance.Id)).InvokeToken!;
        var runId = await _worker.DispatchAndRun(token);
        await _worker.Age(runId, TimeSpan.FromDays(1));

        await Cleanup(cleaner);

        (await RunExists(runId))
            .Should()
            .BeTrue(
                $"the run is past its retention, but its outcome has not been delivered and is read from it. See {Adr}"
            );
        (await _api.Entry(token)).Should().NotBeNull();
        (await _api.Sweep())
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(new { To = "Done", Applied = "done" });
        Context(await SystemRow(instance.Id))["artifact"]!
            .GetValue<string>()
            .Should()
            .Be("artifact:kept");

        // Delivered, the token is cleared, and the run goes as any other expired run does.
        await Cleanup(cleaner);

        (await RunExists(runId)).Should().BeFalse();
        (await _api.Entry(token)).Should().BeNull();
    }

    private static async Task Cleanup(ClusterHost host)
    {
        using var scope = host.Services.CreateScope();
        await scope
            .ServiceProvider.GetRequiredService<IMetadataCleanupTrain>()
            .Run(new MetadataCleanupRequest());
    }

    private async Task<bool> RunExists(long runId)
    {
        using var scope = _api.Services.CreateScope();
        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .Metadatas.AsNoTracking()
            .AnyAsync(m => m.Id == runId);
    }

    // Deletes a finished run with everything it owns, as metadata retention does, whoever holds its token.
    private async Task DeleteRun(long runId)
    {
        using var scope = _api.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IDataContext>();
        await context.WorkQueues.Where(w => w.MetadataId == runId).ExecuteDeleteAsync();
        await context.Logs.Where(l => l.MetadataId == runId).ExecuteDeleteAsync();
        (await context.Metadatas.Where(m => m.Id == runId).ExecuteDeleteAsync()).Should().Be(1);
    }

    private async Task DeleteEntry(string token)
    {
        using var scope = _api.Services.CreateScope();
        (
            await scope
                .ServiceProvider.GetRequiredService<IDataContext>()
                .WorkQueues.Where(w => w.ExternalId == token)
                .ExecuteDeleteAsync()
        )
            .Should()
            .Be(1);
    }

    [Test]
    public async Task The_reconciler_applies_nothing_for_a_run_still_in_progress()
    {
        var instance = await _api.Start(StepMachine.Context(InvokedStepModes.Gate));
        var token = (await SystemRow(instance.Id)).InvokeToken!;

        // Queued, not dispatched.
        (await _api.Sweep())
            .Should()
            .BeEmpty();
        (await _api.Deliver(token)).Should().BeOfType<InvokeDelivery.Running>();

        // Running.
        var runId = await _worker.Dispatch(token);
        var job = _worker.RunJob(runId);
        await InvokedStepGate.Entered.WaitAsync(Bound);
        (await _api.Sweep()).Should().BeEmpty();
        (await _api.Deliver(token)).Should().BeOfType<InvokeDelivery.Running>();
        var row = await SystemRow(instance.Id);
        row.State.Should().Be("Running");
        row.InvokeToken.Should().Be(token);

        InvokedStepGate.Release();
        await Settled(job);
        (await _api.Sweep())
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(new { To = "Done" });
    }

    [Test]
    public async Task The_stored_output_is_on_no_operator_surface()
    {
        // No host here stores parameters, so the run's output is only in the copy its machine reads.
        var note = Guid.NewGuid().ToString("N");
        var secret = InvokedStepModes.Reversed(note);
        var instance = await _api.Start(StepMachine.Context(InvokedStepModes.Secret, note: note));
        var token = (await SystemRow(instance.Id)).InvokeToken!;
        var runId = await _worker.DispatchAndRun(token);
        var entry = await _api.Entry(token);
        var run = await _worker.Run(runId);

        run.Output.Should().BeNull("the hosts store no parameters");
        JsonSerializer
            .Serialize(run)
            .Should()
            .NotContain(
                secret,
                "the run's serialized form, which lifecycle events carry, leaves it out"
            );

        await NoOperatorViewShows(secret, instance.Id, entry!.Id);

        // The machine read it, so it was stored: the sweep reduces it into the context, which no operator view
        // returns either.
        (await _api.Sweep())
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(new { To = "Done" });
        Context(await SystemRow(instance.Id))["artifact"]!.GetValue<string>().Should().Be(secret);
        await NoOperatorViewShows(secret, instance.Id, entry.Id);
    }

    private async Task NoOperatorViewShows(string secret, Guid instance, long entry)
    {
        using var scope = _worker.Services.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<IOperationsService>();
        var views = new object?[]
        {
            await operations.GetWorkQueueEntryDetailAsync(entry, CancellationToken.None),
            await operations.GetMachineInstanceAsync(
                new MachineInstanceKey(
                    SystemStepMachine.MachineId,
                    SnapshotOwnerKind.System,
                    instance
                ),
                CancellationToken.None
            ),
            await operations.GetMachineInstancesAsync(
                new MachineInstanceQuery(SystemStepMachine.MachineId),
                CancellationToken.None
            ),
        };
        views.Should().AllSatisfy(v => v.Should().NotBeNull());
        foreach (var view in views)
            JsonSerializer
                .Serialize(view)
                .Should()
                .NotContain(
                    secret,
                    $"the output a machine reads is never shown to an operator. See {Adr}"
                );
    }

    [Test]
    public async Task A_run_on_a_host_that_registers_the_machine_is_delivered_by_its_hook()
    {
        var instance = await _api.Start(StepMachine.Context(InvokedStepModes.Ok, note: "hook"));
        await _full.DispatchAndRun((await SystemRow(instance.Id)).InvokeToken!);

        var row = await SystemRow(instance.Id);
        row.State.Should().Be("Done", "the run's own host delivered the outcome as it finished");
        Context(row)["artifact"]!.GetValue<string>().Should().Be("artifact:hook");
        (await _api.Sweep()).Should().BeEmpty();
    }

    [Test]
    public async Task The_hosted_reconciler_sweeps_at_its_interval()
    {
        await using var swept = _cluster.Host(
            machines: true,
            scheduler: false,
            options: o => o.InvokeOutcomeSweepInterval = TimeSpan.FromMilliseconds(100)
        );
        var instance = await _api.Start(StepMachine.Context(InvokedStepModes.Ok));
        var token = (await SystemRow(instance.Id)).InvokeToken!;
        var delivered = new TaskCompletionSource<InvokeDelivery>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        swept.Reconciler.Delivered += (run, result) =>
        {
            if (run == token)
                delivered.TrySetResult(result);
        };

        await swept.Reconciler.StartAsync(CancellationToken.None);
        try
        {
            await _worker.DispatchAndRun(token);

            (await delivered.Task.WaitAsync(Bound))
                .Should()
                .BeEquivalentTo(
                    new { To = "Done" },
                    "the running service's sweep found the ended run"
                );
        }
        finally
        {
            await swept.Reconciler.StopAsync(CancellationToken.None);
        }

        (await SystemRow(instance.Id)).State.Should().Be("Done");
    }

    private async Task SweepExpecting(Guid instance, string state)
    {
        (await _api.Sweep())
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(new { To = state, Applied = "cancelled" });
        (await SystemRow(instance)).State.Should().Be(state);
    }

    // A job the test let run to its end, whatever it threw, within the bound.
    private static Task Settled(Task job) =>
        job.ContinueWith(_ => { }, TaskScheduler.Default).WaitAsync(Bound);

    private async Task<SnapshotDraft> SystemRow(Guid id) =>
        (await _api.Row(id, SystemStepMachine.MachineId))!;

    private async Task<SnapshotDraft> UserRow(Guid id) =>
        (await _api.Row(id, UserStepMachine.MachineId))!;

    private static JsonObject Context(SnapshotDraft row) => JsonNode.Parse(row.Context)!.AsObject();

    private static string NewUser() => "user-" + Guid.NewGuid().ToString("N");
}
