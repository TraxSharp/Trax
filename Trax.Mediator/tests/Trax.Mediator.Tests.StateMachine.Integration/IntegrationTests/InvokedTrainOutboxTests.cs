using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.QueuedWorkListener;
using Trax.Effect.Data.Testing;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.StateMachine.Persistence;
using Trax.Effect.StateMachine.Persistence.Mutations;
using Trax.Mediator.Services.TrainBus;
using Trax.Mediator.Tests.StateMachine.Integration.Fakes;
using Trax.Mediator.Tests.StateMachine.Integration.Fixtures;

namespace Trax.Mediator.Tests.StateMachine.Integration.IntegrationTests;

/// <summary>
/// Entering an invoking state queues its run in the transaction that advances the snapshot, through the caller's own
/// data context, and sets the row's invoke token to the run's external id; leaving the state cancels the run and
/// clears the token in the same write. A fault anywhere in between leaves neither half. See
/// <c>Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md</c>.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
public class InvokedTrainOutboxTests(StoreProvider provider)
{
    private const string Adr =
        "Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md";

    private const string User = "u1";

    private static readonly TimeSpan NoticeTimeout = TimeSpan.FromSeconds(10);

    // How long a dispatcher is watched for a notice that must not come.
    private static readonly TimeSpan QuietWindow = TimeSpan.FromMilliseconds(500);

    private InvokeHost _host = null!;

    [SetUp]
    public void SetUp()
    {
        ObservedLauncher.Fault = LaunchFault.None;
        ObservedLauncher.Observe = null;
        _host = InvokeHost.Create(
            provider,
            new HostOptions { Configure = ObservedLauncher.Install }
        );
    }

    [TearDown]
    public void TearDown()
    {
        ObservedLauncher.Fault = LaunchFault.None;
        ObservedLauncher.Observe = null;
        _host.Dispose();
    }

    [Test]
    public async Task Entering_an_invoking_state_queues_one_run_and_sets_the_token_to_its_ExternalId()
    {
        var id = Guid.NewGuid();

        (await _host.EnterRunning(User, id, GoodMachine.MachineId))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Which.Snapshot.State.Should()
            .Be("Running");

        var row = await _host.Row(id, GoodMachine.MachineId);
        var runs = await _host.Runs(id);
        var run = runs.Should().ContainSingle($"one entry queues one run. See {Adr}").Subject;
        row!.InvokeToken.Should().Be(run.ExternalId, "the token names the run the entry queued");
        run.TrainName.Should().Be(typeof(IGoodTrain).FullName);
        run.Status.Should().Be(WorkQueueStatus.Queued);
        run.ConfirmedAt.Should().NotBeNull("an invoked run is never staged");
        run.InvokingMachine.Should().Be(GoodMachine.MachineId);
        run.InvokingOwnerKind.Should().Be(SnapshotOwnerKind.User);
        run.Input.Should()
            .Contain("repo", "the input is built from the context the state was entered with");
    }

    [Test]
    public async Task A_fault_between_the_advance_and_the_enqueue_commits_neither()
    {
        foreach (var fault in new[] { LaunchFault.BeforeEnqueue, LaunchFault.AfterEnqueue })
        {
            var id = Guid.NewGuid();
            ObservedLauncher.Fault = fault;

            var enter = () => _host.EnterRunning(User, id, GoodMachine.MachineId);
            await enter.Should().ThrowAsync<InvalidOperationException>();

            var row = await _host.Row(id, GoodMachine.MachineId);
            row!
                .State.Should()
                .Be("Idle", $"the advance is rolled back with the enqueue ({fault}). See {Adr}");
            row.InvokeToken.Should().BeNull();
            (await _host.Runs(id))
                .Should()
                .BeEmpty($"no run is queued for a machine that never moved ({fault})");
        }

        // The data context the request shares is left clean: the next entry commits normally.
        ObservedLauncher.Fault = LaunchFault.None;
        var next = Guid.NewGuid();
        (await _host.EnterRunning(User, next, GoodMachine.MachineId))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();
        (await _host.Runs(next)).Should().ContainSingle();
    }

    [Test]
    public async Task Progress_saves_cannot_commit_or_abort_the_outbox_part_way()
    {
        foreach (var fault in new[] { LaunchFault.None, LaunchFault.AfterEnqueue })
        {
            var id = Guid.NewGuid();
            TestPrincipal.Become(User);
            using (var scope = _host.Scope())
                await _host
                    .Service(scope, GoodMachine.MachineId)
                    .Autosave(
                        User,
                        id,
                        StageMachine<IGoodTrain, GoodInput, JobOutput>.Json(
                            GoodMachine.MachineId,
                            "Idle"
                        )
                    );

            // Observed from another connection while the outbox's transaction is open: the advance train's own
            // run record has been saved (and committed) through its effect runner, and nothing the outbox wrote is
            // visible yet.
            Metadata? trainRun = null;
            string? stateSeen = null;
            var runsSeen = -1;
            ObservedLauncher.Fault = fault;
            ObservedLauncher.Observe = async () =>
            {
                using var other = _host.Scope();
                var db = other.ServiceProvider.GetRequiredService<IDataContext>();
                trainRun = await db
                    .Metadatas.AsNoTracking()
                    .Where(m => m.Name == typeof(IAdvanceSnapshot).FullName)
                    .OrderByDescending(m => m.Id)
                    .FirstAsync();
                stateSeen = (
                    await db
                        .SnapshotDrafts.AsNoTracking()
                        .SingleAsync(x => x.Id == id && x.Machine == GoodMachine.MachineId)
                ).State;
                runsSeen = await db
                    .WorkQueues.AsNoTracking()
                    .CountAsync(w => w.InvokingInstanceId == id);
            };

            using (var scope = _host.Scope())
            {
                var bus = scope.ServiceProvider.GetRequiredService<ITrainBus>();
                var advance = () =>
                    bus.RunAsync<AdvanceSnapshotOutput>(
                        new AdvanceSnapshotInput
                        {
                            Machine = GoodMachine.MachineId,
                            Id = id,
                            Trigger = "Go",
                        }
                    );
                if (fault == LaunchFault.None)
                    (await advance()).Problem.Should().BeNull();
                else
                    await advance.Should().ThrowAsync<Exception>();
            }

            trainRun.Should().NotBeNull("the observation ran inside the outbox's transaction");
            trainRun!.TrainState.Should().Be(TrainState.InProgress);
            stateSeen.Should().Be("Idle", $"the snapshot is not committed part-way. See {Adr}");
            runsSeen.Should().Be(0, "the run is not committed part-way");

            using var check = _host.Scope();
            var finished = await check
                .ServiceProvider.GetRequiredService<IDataContext>()
                .Metadatas.AsNoTracking()
                .SingleAsync(m => m.Id == trainRun.Id);
            var row = await _host.Row(id, GoodMachine.MachineId);
            if (fault == LaunchFault.None)
            {
                finished.TrainState.Should().Be(TrainState.Completed);
                row!.State.Should().Be("Running");
                (await _host.Runs(id)).Should().ContainSingle();
            }
            else
            {
                finished
                    .TrainState.Should()
                    .Be(
                        TrainState.Failed,
                        "the train's own failure is recorded through its own context"
                    );
                row!.State.Should().Be("Idle", "and it does not commit the outbox's writes");
                (await _host.Runs(id)).Should().BeEmpty();
            }
        }
    }

    [Test]
    public async Task Start_into_an_invoking_initial_state_queues_once()
    {
        var key = MachineKey.Of("partition", Guid.NewGuid().ToString());

        var first = await _host.Start<PartitionMachine>(key);
        var second = await _host.Start<PartitionMachine>(key);

        first.Created.Should().BeTrue();
        second.Created.Should().BeFalse();
        first.State.Should().Be("Running");
        var runs = await _host.Runs(first.Id);
        var run = runs.Should()
            .ContainSingle($"one instance, one entry, one run. See {Adr}")
            .Subject;
        run.InvokingOwnerKind.Should().Be(SnapshotOwnerKind.System);
        (await _host.Row(first.Id, PartitionMachine.MachineId))!
            .InvokeToken.Should()
            .Be(run.ExternalId);
    }

    [Test]
    public async Task Leaving_an_invoking_state_cancels_its_queued_run_and_clears_the_token()
    {
        var id = Guid.NewGuid();
        await _host.EnterRunning(User, id, GoodMachine.MachineId);
        var queued = (await _host.Runs(id)).Single();

        using (var scope = _host.Scope())
            (await _host.Service(scope, GoodMachine.MachineId).Advance(User, id, "Stop"))
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>()
                .Which.Snapshot.State.Should()
                .Be("Idle");

        (await _host.Runs(id))
            .Single()
            .Status.Should()
            .Be(WorkQueueStatus.Cancelled, $"leaving the state cancels its run. See {Adr}");
        (await _host.Row(id, GoodMachine.MachineId))!.InvokeToken.Should().BeNull();

        // Entering again queues a new run under a new token.
        using (var scope = _host.Scope())
            await _host.Service(scope, GoodMachine.MachineId).Advance(User, id, "Go");
        var runs = await _host.Runs(id);
        runs.Should().HaveCount(2);
        runs[1].ExternalId.Should().NotBe(queued.ExternalId);
        (await _host.Row(id, GoodMachine.MachineId))!.InvokeToken.Should().Be(runs[1].ExternalId);
    }

    [LeavesStuckRuns(
        "Dispatches the run by hand and marks it in progress, with no runner; leaving the state only flags its cancel."
    )]
    [Test]
    public async Task Leaving_an_invoking_state_flags_its_dispatched_run_for_cancellation()
    {
        var id = Guid.NewGuid();
        await _host.EnterRunning(User, id, GoodMachine.MachineId);
        var queued = (await _host.Runs(id)).Single();

        // Dispatch it the way the dispatcher does: the entry claimed, and the run recorded under its external id.
        long runId;
        using (var scope = _host.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IDataContext>();
            var run = Metadata.Create(
                new CreateMetadata
                {
                    Name = queued.TrainName,
                    ExternalId = queued.ExternalId,
                    Input = null,
                }
            );
            run.TrainState = TrainState.InProgress;
            await db.Track(run);
            await db.SaveChanges(CancellationToken.None);
            runId = run.Id;
            await db
                .WorkQueues.Where(w => w.Id == queued.Id)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(w => w.Status, WorkQueueStatus.Dispatched)
                        .SetProperty(w => w.MetadataId, runId)
                );
        }

        using (var scope = _host.Scope())
            await _host.Service(scope, GoodMachine.MachineId).Advance(User, id, "Stop");

        using var check = _host.Scope();
        (
            await check
                .ServiceProvider.GetRequiredService<IDataContext>()
                .Metadatas.AsNoTracking()
                .SingleAsync(m => m.Id == runId)
        )
            .CancellationRequested.Should()
            .BeTrue(
                $"a dispatched run is cancelled through its flag, on whichever host runs it. See {Adr}"
            );
        (await _host.Runs(id)).Single().Status.Should().Be(WorkQueueStatus.Dispatched);
    }

    [Test]
    public async Task A_committed_entry_wakes_a_dispatcher_and_a_rolled_back_one_does_not()
    {
        var listener = _host.Services.GetRequiredService<IQueuedWorkListener>();
        await using var subscription = await listener.SubscribeAsync(CancellationToken.None);

        ObservedLauncher.Fault = LaunchFault.AfterEnqueue;
        var refused = () => _host.EnterRunning(User, Guid.NewGuid(), GoodMachine.MachineId);
        await refused.Should().ThrowAsync<InvalidOperationException>();
        var quiet = () => subscription.WaitAsync(new CancellationTokenSource(QuietWindow).Token);
        await quiet
            .Should()
            .ThrowAsync<OperationCanceledException>("a rolled-back entry wakes nothing");

        ObservedLauncher.Fault = LaunchFault.None;
        await _host.EnterRunning(User, Guid.NewGuid(), GoodMachine.MachineId);

        var woken = () => subscription.WaitAsync(new CancellationTokenSource(NoticeTimeout).Token);
        await woken
            .Should()
            .NotThrowAsync("the committed entry tells a waiting dispatcher there is work");
    }
}
