using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Models.SnapshotDraft;
using Trax.Effect.StateMachine.Persistence;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.JobDispatcher;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests.InvokedTrains;

/// <summary>
/// An operator's view of and one action on a state-machine instance, through
/// <see cref="IOperationsService"/>, the path the dashboard and the API share: the runs an instance
/// invoked, and the cancel of a system-owned instance, which cancels its live run and moves it through
/// its state's <c>OnCancelled</c> edge exactly once, however the cancel meets the dispatcher and
/// whichever delivery applies the outcome. Here because the dispatcher, the job runner and the
/// reconciler are reachable here. See
/// <c>Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md</c>.
/// </summary>
[TestFixture(ClusterStore.Postgres)]
[TestFixture(ClusterStore.Sqlite)]
[NonParallelizable]
public class InvokedRunOperationsTests(ClusterStore store)
{
    private const string Adr =
        "Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md";

    // Every wait in this class: long past any step here, short enough that a hang fails the test.
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    // How many times the race is run. Each iteration is one queued run, one dispatcher pass and one
    // cancel, released together.
    private const int RaceIterations = 25;

    private InvokeCluster _cluster = null!;

    // Registers the machines (their hook and reconciler). No scheduler.
    private ClusterHost _api = null!;

    // Dispatches and runs trains and registers no machines: its operations service cancels, and
    // cannot apply an outcome.
    private ClusterHost _worker = null!;

    // Dispatches and runs trains and registers the machines: its operations service applies a
    // queued run's cancel in the call.
    private ClusterHost _full = null!;

    [OneTimeSetUp]
    public async Task CreateCluster()
    {
        _cluster = await InvokeCluster.Create(store);
        _api = _cluster.Host(machines: true, scheduler: false);
        _worker = _cluster.Host(machines: false, scheduler: true);
        _full = _cluster.Host(machines: true, scheduler: true);
    }

    [OneTimeTearDown]
    public async Task DisposeCluster()
    {
        await _api.DisposeAsync();
        await _worker.DisposeAsync();
        await _full.DisposeAsync();
        await _cluster.DisposeAsync();
    }

    [SetUp]
    public async Task Reset()
    {
        InvokedStepGate.Reset();
        await _cluster.Reset();
    }

    [TearDown]
    public void ReleaseGate() => InvokedStepGate.Release();

    [Test]
    public async Task An_operator_cancel_racing_dispatch_of_a_queued_run_ends_exactly_one_way()
    {
        var neverStarted = 0;
        var cancelledRunning = 0;

        for (var i = 0; i < RaceIterations; i++)
        {
            var instance = await _api.Start(StepMachine.Context(InvokedStepModes.Ok));
            var token = (await SystemRow(instance.Id)).InvokeToken!;

            // The cancel reads the instance's token, then signals at its seam just before its
            // conditional statement. On even iterations the dispatcher starts at that signal and
            // the two race; on odd ones the dispatcher's whole pass runs there first, so its claim
            // lands between the cancel's read and its statement.
            var go = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var dispatch = Task.Run(async () =>
            {
                await go.Task;
                using var scope = _worker.Services.CreateScope();
                await scope
                    .ServiceProvider.GetRequiredService<IJobDispatcherTrain>()
                    .Run(Unit.Default);
            });
            var claimFirst = i % 2 == 1;
            var cancel = Task.Run(async () =>
            {
                using var scope = _worker.Services.CreateScope();
                var operations = (OperationsService)
                    scope.ServiceProvider.GetRequiredService<IOperationsService>();
                operations.BeforeMachineInstanceCancel = async _ =>
                {
                    go.SetResult();
                    if (claimFirst)
                        await dispatch;
                };
                return await operations.CancelMachineInstanceAsync(
                    SystemKey(instance.Id),
                    CancellationToken.None
                );
            });
            await Task.WhenAll(dispatch, cancel).WaitAsync(Bound);

            var entry = (await _api.Entry(token))!;
            var run = await RunByExternalId(token);

            if (entry.Status == WorkQueueStatus.Cancelled)
            {
                run.Should()
                    .BeNull(
                        $"a cancelled entry is never dispatched, so its run never starts. See {Adr}"
                    );
                cancel.Result.Outcome.Should().Be(MachineInstanceCancelOutcome.RunCancelled);
                neverStarted++;
            }
            else
            {
                entry.Status.Should().Be(WorkQueueStatus.Dispatched);
                run.Should().NotBeNull("the claim and the run's row commit together");
                run!
                    .CancellationRequested.Should()
                    .BeTrue(
                        $"a cancel that finds the entry claimed flags the run the claim wrote: "
                            + $"never neither. See {Adr}"
                    );
                cancel.Result.Outcome.Should().Be(MachineInstanceCancelOutcome.CancelRequested);

                await _worker.RunJob(run.Id);
                (await _worker.Run(run.Id))
                    .TrainState.Should()
                    .Be(TrainState.Cancelled, "a flagged run ends Cancelled");
                cancelledRunning++;
            }

            (await _api.Deliver(token))
                .Should()
                .BeOfType<InvokeDelivery.Moved>()
                .Which.To.Should()
                .Be("Cancelled", "either way the instance moves through OnCancelled");
            (await _api.Deliver(token))
                .Should()
                .BeOfType<InvokeDelivery.NoTransition>("and only once");
            var row = await SystemRow(instance.Id);
            row.State.Should().Be("Cancelled");
            row.InvokeToken.Should().BeNull();
        }

        (neverStarted + cancelledRunning).Should().Be(RaceIterations);
        cancelledRunning
            .Should()
            .BeGreaterThanOrEqualTo(
                RaceIterations / 2,
                "every claim that lands before the statement leaves a run to flag"
            );
        TestContext.Out.WriteLine(
            $"{neverStarted} cancelled before dispatch, {cancelledRunning} cancelled after it."
        );
    }

    [Test]
    public async Task An_operators_cancel_of_a_queued_run_moves_the_instance_through_OnCancelled_once()
    {
        var instance = await _api.Start(StepMachine.Context(InvokedStepModes.Ok));
        var token = (await SystemRow(instance.Id)).InvokeToken!;

        var result = await Cancel(_full, SystemKey(instance.Id));

        result.Outcome.Should().Be(MachineInstanceCancelOutcome.Moved);
        result.Success.Should().BeTrue();
        result.State.Should().Be("Cancelled");
        result
            .Message.Should()
            .Be(OperationsService.MovedMessage(SystemKey(instance.Id), "Cancelled"));
        (await _api.Entry(token))!.Status.Should().Be(WorkQueueStatus.Cancelled);
        (await RunByExternalId(token)).Should().BeNull("the run never started");

        var row = await SystemRow(instance.Id);
        row.State.Should().Be("Cancelled", $"the state's OnCancelled target. See {Adr}");
        row.InvokeToken.Should().BeNull();
        (await _api.Sweep()).Should().BeEmpty("the cancel applied the outcome, once");
        (await _api.Deliver(token)).Should().BeOfType<InvokeDelivery.NoTransition>();

        var again = await Cancel(_full, SystemKey(instance.Id));
        again.Outcome.Should().Be(MachineInstanceCancelOutcome.NoLiveRun);
        again.Success.Should().BeFalse();
        again
            .Message.Should()
            .Be(OperationsService.NoLiveRunMessage(SystemKey(instance.Id), "Cancelled"));
    }

    [Test]
    public async Task On_a_host_without_the_machine_the_cancel_is_applied_by_the_reconciler()
    {
        var instance = await _api.Start(StepMachine.Context(InvokedStepModes.Ok));
        var token = (await SystemRow(instance.Id)).InvokeToken!;

        var result = await Cancel(_worker, SystemKey(instance.Id));

        result.Outcome.Should().Be(MachineInstanceCancelOutcome.RunCancelled);
        result.Success.Should().BeTrue();
        (await SystemRow(instance.Id))
            .State.Should()
            .Be("Running", "this host cannot read the machine, so it applies nothing");

        (await _api.Sweep())
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(new { To = "Cancelled", Applied = "cancelled" });
        (await SystemRow(instance.Id)).State.Should().Be("Cancelled");
        (await _api.Deliver(token)).Should().BeOfType<InvokeDelivery.NoTransition>();
    }

    [Test]
    public async Task An_operators_cancel_stops_its_live_run_and_the_instance_reaches_the_OnCancelled_target()
    {
        var instance = await _api.Start(StepMachine.Context(InvokedStepModes.Gate));
        var token = (await SystemRow(instance.Id)).InvokeToken!;
        var runId = await _worker.Dispatch(token);
        var job = _worker.RunJob(runId);
        await InvokedStepGate.Entered.WaitAsync(Bound);

        var result = await Cancel(_worker, SystemKey(instance.Id));

        result.Outcome.Should().Be(MachineInstanceCancelOutcome.CancelRequested);
        result
            .Message.Should()
            .Be(OperationsService.CancelRequestedMessage(SystemKey(instance.Id), "Running"));
        InvokedStepGate.Release();
        await job.ContinueWith(_ => { }, TaskScheduler.Default).WaitAsync(Bound);

        (await _worker.Run(runId))
            .TrainState.Should()
            .Be(TrainState.Cancelled, $"the run stopped at its next junction. See {Adr}");
        (await _api.Sweep())
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(new { To = "Cancelled", Applied = "cancelled" });
        var row = await SystemRow(instance.Id);
        row.State.Should().Be("Cancelled");
        row.InvokeToken.Should().BeNull();
    }

    [Test]
    public async Task A_cancel_of_a_user_owned_instance_is_refused_and_changes_nothing()
    {
        var user = "user-" + Guid.NewGuid().ToString("N");
        var id = Guid.NewGuid();
        (await _api.EnterRunning(user, id, StepMachine.Context(InvokedStepModes.Ok)))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();
        var row = (await _api.Row(id, UserStepMachine.MachineId))!;

        var result = await Cancel(
            _full,
            new MachineInstanceKey(UserStepMachine.MachineId, SnapshotOwnerKind.User, id, row.RowId)
        );

        result.Outcome.Should().Be(MachineInstanceCancelOutcome.UserOwned);
        result.Success.Should().BeFalse();
        result
            .Message.Should()
            .Be(
                OperationsService.UserOwnedCancelRefusal,
                $"operators see a user's draft read-only. See {Adr}"
            );
        var after = (await _api.Row(id, UserStepMachine.MachineId))!;
        after.State.Should().Be("Running");
        after.InvokeToken.Should().Be(row.InvokeToken);
        (await _api.Entry(row.InvokeToken!))!.Status.Should().Be(WorkQueueStatus.Queued);
    }

    [Test]
    public async Task A_cancel_of_no_instance_or_of_a_run_that_has_ended_is_refused()
    {
        var missing = await Cancel(_full, SystemKey(Guid.NewGuid()));
        missing.Outcome.Should().Be(MachineInstanceCancelOutcome.NotFound);
        missing.Success.Should().BeFalse();

        // Ended on a host that delivers nothing, so the instance still holds the token.
        var instance = await _api.Start(StepMachine.Context(InvokedStepModes.Ok));
        var token = (await SystemRow(instance.Id)).InvokeToken!;
        await _worker.DispatchAndRun(token);

        var ended = await Cancel(_worker, SystemKey(instance.Id));

        ended.Outcome.Should().Be(MachineInstanceCancelOutcome.RunEnded);
        ended.Success.Should().BeFalse();
        (await _api.Sweep())
            .Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(new { To = "Done" }, "the run's own outcome is applied, not a cancel");
    }

    [Test]
    public async Task A_system_instance_lists_its_runs_newest_first_with_the_live_one_marked()
    {
        var instance = await _api.Start(StepMachine.Context(InvokedStepModes.Ok));
        var key = SystemKey(instance.Id);
        var first = (await SystemRow(instance.Id)).InvokeToken!;

        var queued = await Runs(key);
        queued.Items.Should().BeEmpty("the live run is still only a work queue entry");
        queued.QueuedEntryId.Should().Be((await _api.Entry(first))!.Id);

        var firstRun = await _full.DispatchAndRun(first);
        (await SystemRow(instance.Id)).State.Should().Be("Done");

        var done = await Runs(key);
        done.Items.Should().ContainSingle().Which.Id.Should().Be(firstRun);
        done.Items[0].IsLive.Should().BeFalse("the state that invoked it was left");
        done.Items[0].TrainState.Should().Be(TrainState.Completed);
        done.Items[0].TrainName.Should().Be(typeof(IInvokedStepTrain).FullName);
        done.QueuedEntryId.Should().BeNull();
        done.Capped.Should().BeFalse();

        var user = "user-" + Guid.NewGuid().ToString("N");
        var draft = Guid.NewGuid();
        await _api.EnterRunning(user, draft, StepMachine.Context(InvokedStepModes.Gate));
        var userRow = (await _api.Row(draft, UserStepMachine.MachineId))!;
        var userRunId = await _worker.Dispatch(userRow.InvokeToken!);
        var userRuns = await Runs(
            new MachineInstanceKey(
                UserStepMachine.MachineId,
                SnapshotOwnerKind.User,
                draft,
                userRow.RowId
            )
        );
        userRuns
            .Items.Should()
            .ContainSingle()
            .Which.Should()
            .BeEquivalentTo(new { Id = userRunId, IsLive = true });
    }

    [Test]
    public async Task A_users_draft_lists_only_its_own_live_run_never_another_users_under_the_same_id()
    {
        var id = Guid.NewGuid();
        var alice = "alice-" + Guid.NewGuid().ToString("N");
        var bob = "bob-" + Guid.NewGuid().ToString("N");

        // Alice's draft runs once and finishes; Bob then holds a draft under the same id, waiting
        // on a run of his own.
        await _api.EnterRunning(alice, id, StepMachine.Context(InvokedStepModes.Ok));
        var aliceToken = (await UserRows(id)).Single().InvokeToken!;
        var aliceRun = await _full.DispatchAndRun(aliceToken);
        await _api.EnterRunning(bob, id, StepMachine.Context(InvokedStepModes.Gate));
        var rows = await UserRows(id);
        var aliceRow = rows.Single(r => r.UserKey == alice);
        var bobRow = rows.Single(r => r.UserKey == bob);
        var bobRun = await _worker.Dispatch(bobRow.InvokeToken!);

        var aliceRuns = await Runs(UserKey(id, aliceRow.RowId));
        var bobRuns = await Runs(UserKey(id, bobRow.RowId));

        aliceRuns
            .Items.Should()
            .BeEmpty(
                "a run does not record whose draft queued it, so only a draft's live run is "
                    + $"listed, and Alice's has none. See {Adr}"
            );
        bobRuns.Items.Should().ContainSingle().Which.Id.Should().Be(bobRun);
        bobRuns.Items.Select(r => r.Id).Should().NotContain(aliceRun);
    }

    private static MachineInstanceKey SystemKey(Guid id) =>
        new(SystemStepMachine.MachineId, SnapshotOwnerKind.System, id);

    private static MachineInstanceKey UserKey(Guid id, long rowId) =>
        new(UserStepMachine.MachineId, SnapshotOwnerKind.User, id, rowId);

    private static async Task<MachineInstanceCancelResult> Cancel(
        ClusterHost host,
        MachineInstanceKey key
    )
    {
        using var scope = host.Services.CreateScope();
        return await scope
            .ServiceProvider.GetRequiredService<IOperationsService>()
            .CancelMachineInstanceAsync(key, CancellationToken.None);
    }

    private async Task<MachineInstanceRuns> Runs(MachineInstanceKey key)
    {
        using var scope = _worker.Services.CreateScope();
        return (
            await scope
                .ServiceProvider.GetRequiredService<IOperationsService>()
                .GetMachineInstanceRunsAsync(key, CancellationToken.None)
        )!;
    }

    private async Task<Trax.Effect.Models.Metadata.Metadata?> RunByExternalId(string token)
    {
        using var scope = _api.Services.CreateScope();
        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .Metadatas.AsNoTracking()
            .SingleOrDefaultAsync(m => m.ExternalId == token);
    }

    private async Task<List<SnapshotDraft>> UserRows(Guid id)
    {
        using var scope = _api.Services.CreateScope();
        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .SnapshotDrafts.AsNoTracking()
            .Where(x => x.Id == id && x.Machine == UserStepMachine.MachineId)
            .ToListAsync();
    }

    private async Task<SnapshotDraft> SystemRow(Guid id) =>
        (await _api.Row(id, SystemStepMachine.MachineId))!;
}
