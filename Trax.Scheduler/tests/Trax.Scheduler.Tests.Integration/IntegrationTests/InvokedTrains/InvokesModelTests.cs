using System.Text.Json.Nodes;
using AwesomeAssertions;
using CsCheck;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Functional;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Testing;
using Trax.Effect.Enums;
using Trax.Effect.Models.SnapshotDraft;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.JobDispatcher;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests.InvokedTrains;

/// <summary>
/// A model-based property over machines whose states invoke trains: generated sequences of user triggers,
/// autosaves, forged outcome triggers, system starts, dispatches, runs, reaps, sweeps, deliveries (late,
/// duplicated, out of order, concurrent, from several hosts), operator cancels, and races of a user's leave with a
/// delivery or a dispatch. Every step drives the real draft service, outbox, dispatcher, job runner, manifest
/// manager and reconciler over one database, and is checked against a small in-memory model of what each
/// instance's state, token and runs should be. After every step: each stored snapshot rehydrates and is in the
/// model's state; at most one live run belongs to an instance and it is the one its token names; no live run is
/// orphaned (it is some instance's token, or its cancel is requested); forged moves change nothing; and each
/// run's outcome is applied at most once.
/// </summary>
/// <remarks>
/// <para>The user-owned machine is <see cref="UserStepMachine"/>, which a user drives through <c>Go</c>,
/// <c>Stop</c>, <c>Retry</c> and <c>Continue</c>; the system-owned one is <see cref="SystemStepMachine"/>, whose
/// <c>chain</c> outcome enters a second invoking state. Hosts are those of <see cref="InvokeCluster"/>: two API hosts
/// with the machines, a worker without them (so nothing it runs is delivered by a hook), and a full host whose hook
/// delivers what it runs.</para>
/// <para>A failure prints the seed CsCheck shrank it to and the operations. Pin it as its own test with
/// <c>SampleAsync(..., seed: "...")</c> before fixing the code, so the case keeps running.</para>
/// See <c>Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md</c> and
/// <c>Trax.Docs/adr/0044-property-tests-use-cscheck-in-test-projects-only.md</c>.
/// </remarks>
[TestFixture(ClusterStore.Postgres)]
[TestFixture(ClusterStore.Sqlite)]
[NonParallelizable]
public class InvokesModelTests(ClusterStore store)
{
    private const string Adr =
        "Trax.Docs/adr/0046-a-machine-state-invokes-a-train-and-only-that-entry-receives-its-outcome.md";

    // Sequences per store: each is up to MaxSteps operations against a fresh database, checked after every one.
    private const int Iterations = 300;

    private const int MinSteps = 4;

    private const int MaxSteps = 40;

    // Every wait on a concurrent step: long past any step here, short enough that a hang fails the test.
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    // The TraxInvariants that hold between any two steps, not only once every host has stopped: the dispatcher
    // writes a run with its dispatched entry, and an instance's token with the state it is in and the run it queued.
    private static readonly HashSet<string> AlwaysHold =
    [
        TraxInvariants.DispatchedWithoutRun,
        TraxInvariants.InvokeTokenWithoutRun,
        TraxInvariants.InvokingStateWithoutToken,
        TraxInvariants.InvokeTokenOutsideInvokingState,
    ];

    private InvokeCluster _cluster = null!;
    private ClusterHost _api = null!;
    private ClusterHost _api2 = null!;
    private ClusterHost _worker = null!;
    private ClusterHost _full = null!;

    // An API host whose drafts expire after an hour idle, so a load there deletes an aged draft and its run's hold.
    private ClusterHost _expiring = null!;

    [OneTimeSetUp]
    public async Task CreateCluster()
    {
        _cluster = await InvokeCluster.Create(store);
        _api = _cluster.Host(machines: true, scheduler: false);
        _api2 = _cluster.Host(machines: true, scheduler: false);
        _worker = _cluster.Host(machines: false, scheduler: true);
        _full = _cluster.Host(machines: true, scheduler: true);
        _expiring = _cluster.Host(
            machines: true,
            scheduler: false,
            options: o => o.DraftTtl = TimeSpan.FromHours(1)
        );
    }

    [OneTimeTearDown]
    public async Task DisposeCluster()
    {
        await _api.DisposeAsync();
        await _api2.DisposeAsync();
        await _worker.DisposeAsync();
        await _full.DisposeAsync();
        await _expiring.DisposeAsync();
        await _cluster.DisposeAsync();
    }

    [Test]
    public Task Generated_triggers_autosaves_and_completions_keep_every_snapshot_valid_and_orphan_no_run() =>
        AnOperation
            .Array[MinSteps, MaxSteps]
            .SampleAsync(
                async ops =>
                {
                    await _cluster.Reset();
                    await new World(this).Run(ops);
                },
                iter: Iterations,
                threads: 1,
                print: Describe
            );

    #region Operations

    private enum Kind
    {
        UserSave,
        UserAdvance,
        UserForgeSave,
        UserForgeOutcome,
        SystemStart,
        Dispatch,
        RunJob,
        Reap,
        Sweep,
        Deliver,
        DeliverConcurrently,
        OperatorCancelInstance,
        OperatorCancelRun,
        RaceLeaveAndDeliver,
        RaceLeaveAndDispatch,
        RaceLeaveAndRun,
        SystemStartConcurrently,
        ExpireDraft,
    }

    /// <summary>One generated step. <see cref="A"/> and <see cref="B"/> pick its subject and variant, modulo what exists.</summary>
    private readonly record struct Op(Kind Kind, int A, int B)
    {
        public override string ToString() =>
            Kind switch
            {
                Kind.UserSave => $"UserSave(u{A % 2}, {Modes[B % Modes.Length]})",
                Kind.UserAdvance => $"UserAdvance(u{A % 2}, {Triggers[B % Triggers.Length]})",
                Kind.UserForgeSave => $"UserForgeSave(u{A % 2}, {Forged[B % Forged.Length]})",
                Kind.UserForgeOutcome =>
                    $"UserForgeOutcome(u{A % 2}, {OutcomeTriggers[B % OutcomeTriggers.Length]})",
                Kind.SystemStart => $"SystemStart(s{A % 2}, {Modes[B % Modes.Length]})",
                Kind.Dispatch => $"Dispatch(on {RunnerName(A)})",
                Kind.RunJob => $"RunJob(dispatched #{A})",
                Kind.Reap => $"Reap(dispatched #{A}, on {RunnerName(B)})",
                Kind.Sweep => $"Sweep(on api{A % 2 + 1})",
                Kind.Deliver => $"Deliver(run #{A}, on {DelivererName(B)})",
                Kind.DeliverConcurrently => $"DeliverConcurrently(run #{A})",
                Kind.OperatorCancelInstance =>
                    $"OperatorCancelInstance({InstanceName(A)}, on {RunnerName(B)})",
                Kind.OperatorCancelRun => $"OperatorCancelRun(run #{A}, on {RunnerName(B)})",
                Kind.RaceLeaveAndDeliver => $"RaceLeaveAndDeliver(u{A % 2})",
                Kind.RaceLeaveAndDispatch => $"RaceLeaveAndDispatch(u{A % 2}, on {RunnerName(B)})",
                Kind.RaceLeaveAndRun => $"RaceLeaveAndRun(u{A % 2})",
                Kind.SystemStartConcurrently =>
                    $"SystemStartConcurrently(s{A % 2}, {Modes[B % Modes.Length]})",
                Kind.ExpireDraft => $"ExpireDraft(u{A % 2})",
                _ => Kind.ToString(),
            };
    }

    private static readonly string[] Modes =
    [
        InvokedStepModes.Ok,
        InvokedStepModes.Chain,
        InvokedStepModes.Unaccepted,
        InvokedStepModes.Fail,
    ];

    // Go most often, so drafts reach their runs; the rest often enough to leave, retry and chain.
    private static readonly StepTrigger[] Triggers =
    [
        StepTrigger.Go,
        StepTrigger.Go,
        StepTrigger.Go,
        StepTrigger.Stop,
        StepTrigger.Stop,
        StepTrigger.Retry,
        StepTrigger.Retry,
        StepTrigger.Continue,
    ];

    // The states a user's autosave may never move a draft into: the invoking states and every outcome target.
    private static readonly string[] Forged = ["Running", "Chained", "Done", "Failed", "Cancelled"];

    private static readonly string[] OutcomeTriggers =
    [
        "Running.done",
        "Running.failed",
        "Running.cancelled",
        "Chained.done",
        "Chained.failed",
        "Chained.cancelled",
    ];

    private static string RunnerName(int i) => i % 2 == 0 ? "worker" : "full";

    private static string DelivererName(int i) =>
        (i % 3) switch
        {
            0 => "api",
            1 => "api2",
            _ => "full",
        };

    private static string InstanceName(int i) =>
        (i % 4) switch
        {
            0 => "u0",
            1 => "u1",
            2 => "s0",
            _ => "s1",
        };

    private static Gen<Op> Of(Kind kind, int a, int b) =>
        Gen.Select(Gen.Int[0, a], Gen.Int[0, b]).Select(t => new Op(kind, t.Item1, t.Item2));

    // Weighted so that runs are entered, dispatched and run often enough for their completions to meet the rest.
    private static readonly Gen<Op> AnOperation = Gen.Frequency(
        (6, Of(Kind.UserSave, 1, 3)),
        (10, Of(Kind.UserAdvance, 1, 7)),
        (2, Of(Kind.UserForgeSave, 1, 4)),
        (2, Of(Kind.UserForgeOutcome, 1, 5)),
        (4, Of(Kind.SystemStart, 1, 3)),
        (8, Of(Kind.Dispatch, 1, 0)),
        (8, Of(Kind.RunJob, 3, 0)),
        (2, Of(Kind.Reap, 3, 1)),
        (4, Of(Kind.Sweep, 1, 0)),
        (5, Of(Kind.Deliver, 7, 2)),
        (3, Of(Kind.DeliverConcurrently, 7, 0)),
        (2, Of(Kind.OperatorCancelInstance, 3, 1)),
        (2, Of(Kind.OperatorCancelRun, 7, 1)),
        (3, Of(Kind.RaceLeaveAndDeliver, 1, 0)),
        (3, Of(Kind.RaceLeaveAndDispatch, 1, 1)),
        (3, Of(Kind.RaceLeaveAndRun, 1, 0)),
        (2, Of(Kind.SystemStartConcurrently, 1, 3)),
        (2, Of(Kind.ExpireDraft, 1, 0))
    );

    private static string Describe(Op[] ops) =>
        string.Join(Environment.NewLine, ops.Select((op, i) => $"  {i}: {op}"));

    #endregion

    #region Model

    private enum Phase
    {
        // Its work queue entry waits for the dispatcher.
        Queued,

        // Its entry was cancelled before dispatch: it never runs, and its outcome is Cancelled.
        Withdrawn,

        // Dispatched, with a run row still Pending.
        Dispatched,

        // Its run row is terminal.
        Ended,
    }

    private sealed class Instance(string name, bool system)
    {
        public string Name { get; } = name;

        public bool System { get; } = system;

        public string Machine => System ? SystemStepMachine.MachineId : UserStepMachine.MachineId;

        public string UserKey { get; } = $"{name}-{Guid.NewGuid():N}";

        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>Null until the instance exists.</summary>
        public string? State { get; set; }

        public string? Token { get; set; }

        /// <summary>The run the last step's entry into an invoking state queued, whose token the row now holds.</summary>
        public (string Mode, string Note)? Entering { get; set; }

        public string Mode { get; set; } = "";

        public string Note { get; set; } = "";

        public string Artifact { get; set; } = "";
    }

    private sealed class Run(string token, Instance owner, string mode, string note)
    {
        public string Token { get; } = token;

        public Instance Owner { get; } = owner;

        public string Mode { get; } = mode;

        public string Note { get; } = note;

        public Phase Phase { get; set; } = Phase.Queued;

        public long? MetadataId { get; set; }

        public ClusterHost? DispatchedBy { get; set; }

        public bool CancelRequested { get; set; }

        public TrainState? End { get; set; }

        public override string ToString() =>
            $"{Owner.Name}:{Mode}:{Phase}{(CancelRequested ? "+cancel" : "")}{(End is { } e ? ":" + e : "")}";
    }

    /// <summary>One sequence: the model, the real cluster it drives, and the checks after each step.</summary>
    private sealed class World(InvokesModelTests test)
    {
        private readonly Instance[] _users = [new("u0", false), new("u1", false)];
        private readonly Instance[] _systems = [new("s0", true), new("s1", true)];
        private readonly string _keyPrefix = Guid.NewGuid().ToString("N");

        // Every run any step queued, oldest first.
        private readonly List<Run> _runs = [];

        // The tokens whose outcome the model applied, and the ones some caller reported moving an instance.
        private readonly HashSet<string> _applied = [];
        private readonly Dictionary<string, int> _moved = [];

        private int _step;

        private IEnumerable<Instance> Instances => _users.Concat(_systems);

        private ClusterHost Api => test._api;

        private ClusterHost Api2 => test._api2;

        private ClusterHost Runner(int i) => i % 2 == 0 ? test._worker : test._full;

        private ClusterHost Deliverer(int i) =>
            (i % 3) switch
            {
                0 => test._api,
                1 => test._api2,
                _ => test._full,
            };

        public async Task Run(Op[] ops)
        {
            for (_step = 0; _step < ops.Length; _step++)
            {
                var op = ops[_step];
                try
                {
                    await Step(op);
                    await Adopt();
                    await Check();
                }
                catch (Exception ex) when (ex is not StepFailed)
                {
                    throw new StepFailed(
                        $"Step {_step} ({op}) broke the model. Runs: [{string.Join(", ", _runs)}]. "
                            + $"See {Adr}",
                        ex
                    );
                }
            }
        }

        private Task Step(Op op) =>
            op.Kind switch
            {
                Kind.UserSave => UserSave(_users[op.A % 2], Modes[op.B % Modes.Length]),
                Kind.UserAdvance => UserAdvance(_users[op.A % 2], Triggers[op.B % Triggers.Length]),
                Kind.UserForgeSave => UserForgeSave(_users[op.A % 2], Forged[op.B % Forged.Length]),
                Kind.UserForgeOutcome => UserForgeOutcome(
                    _users[op.A % 2],
                    OutcomeTriggers[op.B % OutcomeTriggers.Length]
                ),
                Kind.SystemStart => SystemStart(_systems[op.A % 2], Modes[op.B % Modes.Length]),
                Kind.Dispatch => Dispatch(Runner(op.A)),
                Kind.RunJob => RunJob(op.A),
                Kind.Reap => Reap(op.A, Runner(op.B)),
                Kind.Sweep => Sweep(op.A % 2 == 0 ? Api : Api2),
                Kind.Deliver => Deliver(op.A, Deliverer(op.B)),
                Kind.DeliverConcurrently => DeliverConcurrently(op.A),
                Kind.OperatorCancelInstance => OperatorCancelInstance(
                    Instances.ElementAt(op.A % 4),
                    Runner(op.B)
                ),
                Kind.OperatorCancelRun => OperatorCancelRun(op.A, Runner(op.B)),
                Kind.RaceLeaveAndDeliver => RaceLeaveAndDeliver(_users[op.A % 2]),
                Kind.RaceLeaveAndDispatch => RaceLeaveAndDispatch(_users[op.A % 2], Runner(op.B)),
                Kind.RaceLeaveAndRun => RaceLeaveAndRun(_users[op.A % 2]),
                Kind.SystemStartConcurrently => SystemStartConcurrently(
                    _systems[op.A % 2],
                    Modes[op.B % Modes.Length]
                ),
                Kind.ExpireDraft => ExpireDraft(_users[op.A % 2]),
                _ => throw new ArgumentOutOfRangeException(nameof(op)),
            };

        #region Steps

        private async Task UserSave(Instance user, string mode)
        {
            var note = $"n{_step}";
            var saved = await Drafts(
                Api,
                d =>
                    d.Autosave(
                        user.UserKey,
                        user.Id,
                        StepMachine.Json(
                            UserStepMachine.MachineId,
                            "Idle",
                            StepMachine.Context(mode, note)
                        )
                    )
            );

            // A draft in an invoking state holds its run: autosave neither leaves it nor rewrites its context.
            if (user.State is "Running" or "Chained")
            {
                saved
                    .Should()
                    .BeOfType<AutosaveResult.Rejected>(
                        "autosave never leaves an invoking state, which would strand its run"
                    );
                return;
            }

            saved.Should().BeOfType<AutosaveResult.Saved>();
            user.State = "Idle";
            user.Mode = mode;
            user.Note = note;
            user.Artifact = "";
        }

        private async Task UserForgeSave(Instance user, string state)
        {
            var context = StepMachine.Context(InvokedStepModes.Ok, $"forged{_step}");
            context["artifact"] = "forged";
            var saved = await Drafts(
                Api,
                d =>
                    d.Autosave(
                        user.UserKey,
                        user.Id,
                        StepMachine.Json(UserStepMachine.MachineId, state, context)
                    )
            );

            saved
                .Should()
                .BeOfType<AutosaveResult.Rejected>(
                    $"autosave may not move a draft into {state}, an invoking state or an outcome target"
                );
        }

        private async Task UserForgeOutcome(Instance user, string trigger)
        {
            var advanced = await Drafts(
                Api,
                d =>
                    d.Advance(
                        user.UserKey,
                        user.Id,
                        trigger,
                        new JsonObject
                        {
                            ["artifact"] = "forged",
                            ["accepted"] = true,
                            ["chain"] = false,
                        }
                    )
            );

            if (user.State is null)
                advanced.Should().BeOfType<AdvanceOutcome.NotFound>();
            else
                advanced
                    .Should()
                    .BeOfType<AdvanceOutcome.Rejected>()
                    .Which.Reason.Should()
                    .Be("outcome-bound", "only a run's outcome fires an outcome trigger");
        }

        private async Task UserAdvance(Instance user, StepTrigger trigger)
        {
            var advanced = await Drafts(
                Api,
                d => d.Advance(user.UserKey, user.Id, trigger.ToString())
            );

            if (user.State is null)
            {
                advanced.Should().BeOfType<AdvanceOutcome.NotFound>();
                return;
            }

            var target = (user.State, trigger) switch
            {
                ("Idle", StepTrigger.Go) => "Running",
                ("Running", StepTrigger.Stop) => "Idle",
                ("Failed", StepTrigger.Retry) => "Running",
                ("Cancelled", StepTrigger.Retry) => "Running",
                ("Done", StepTrigger.Continue) => "Chained",
                _ => null,
            };
            if (target is null)
            {
                advanced.Should().BeOfType<AdvanceOutcome.Rejected>();
                return;
            }

            advanced.Should().BeOfType<AdvanceOutcome.Advanced>();
            Move(user, target);
        }

        private async Task SystemStart(Instance system, string mode)
        {
            var note = $"n{_step}";
            MachineInstance started;
            using (var scope = Api.Services.CreateScope())
                started = await scope
                    .ServiceProvider.GetRequiredService<IMachineInstances>()
                    .Start<SystemStepMachine>(
                        MachineKey.Of(_keyPrefix, system.Name),
                        StepMachine.Context(mode, note)
                    );

            if (system.State is null)
            {
                started.Created.Should().BeTrue();
                started.State.Should().Be("Running");
                system.Id = started.Id;
                system.Mode = mode;
                system.Note = note;
                Enter(system, "Running");
                return;
            }

            started
                .Should()
                .BeEquivalentTo(
                    new
                    {
                        system.Id,
                        Created = false,
                        system.State,
                    },
                    "starting an existing instance finds it and queues nothing new"
                );
        }

        private async Task Dispatch(ClusterHost host)
        {
            using (var scope = host.Services.CreateScope())
                await scope
                    .ServiceProvider.GetRequiredService<IJobDispatcherTrain>()
                    .Run(Unit.Default);
            await ObserveDispatch(host);
        }

        private async Task RunJob(int pick)
        {
            if (Pick(_runs.Where(r => r.Phase == Phase.Dispatched), pick) is not { } run)
                return;

            await run.DispatchedBy!.RunJob(run.MetadataId!.Value);

            run.Phase = Phase.Ended;
            run.End =
                run.CancelRequested ? TrainState.Cancelled
                : run.Mode == InvokedStepModes.Fail ? TrainState.Failed
                : TrainState.Completed;

            // The run's own host delivers its outcome through the hook when it registers the machine.
            if (run.DispatchedBy == test._full)
                ApplyIfHeld(run);
        }

        private async Task Reap(int pick, ClusterHost host)
        {
            if (Pick(_runs.Where(r => r.Phase == Phase.Dispatched), pick) is not { } run)
                return;

            await host.Age(run.MetadataId!.Value, TimeSpan.FromHours(1));
            await host.RunManifestManager();

            var reaped = (await host.Run(run.MetadataId.Value)).TrainState;
            reaped
                .Should()
                .Be(TrainState.Failed, "a dispatched run nobody picked up is failed by the reaper");
            run.Phase = Phase.Ended;
            run.End = reaped;

            // The reaper publishes the run's failure, and a host that registers the machine applies it.
            if (host == test._full)
                ApplyIfHeld(run);
        }

        private async Task Sweep(ClusterHost host)
        {
            var expected = Instances.Where(Deliverable).ToList();
            var swept = await host.Sweep();

            foreach (var delivery in swept)
                Count(delivery);
            swept
                .OfType<InvokeDelivery.Moved>()
                .Select(m => m.Id)
                .Should()
                .BeEquivalentTo(
                    expected.Select(i => i.Id),
                    "a sweep applies the outcome of every ended run an instance still waits on, and nothing else"
                );
            swept.Should().AllBeOfType<InvokeDelivery.Moved>();

            foreach (var instance in expected)
                Apply(instance, RunOf(instance.Token!));
        }

        private async Task Deliver(int pick, ClusterHost host)
        {
            if (Pick(_runs, pick) is not { } run)
                return;

            var holder = Holder(run);
            var delivered = await host.Deliver(run.Token);
            Count(delivered);

            if (holder is null)
            {
                delivered
                    .Should()
                    .BeOfType<InvokeDelivery.NoTransition>(
                        "a completion whose token no instance holds is late, duplicated or stale"
                    );
                return;
            }

            if (!Ended(run))
            {
                delivered.Should().BeOfType<InvokeDelivery.Running>();
                return;
            }

            delivered.Should().BeOfType<InvokeDelivery.Moved>();
            Apply(holder, run);
        }

        private async Task DeliverConcurrently(int pick)
        {
            if (Pick(_runs, pick) is not { } run)
                return;

            var others = Instances.Where(Deliverable).ToList();
            var holder = Holder(run);
            var deliverable = holder is not null && Ended(run);

            var deliveries = Task.WhenAll(
                test._api.Deliver(run.Token),
                test._api2.Deliver(run.Token),
                test._full.Deliver(run.Token)
            );
            var sweep = test._api2.Sweep();
            await Task.WhenAll(deliveries, sweep).WaitAsync(Bound);

            deliveries.Result.Should().NotContainNulls("no delivery fails");
            var all = deliveries.Result.Cast<InvokeDelivery>().Concat(sweep.Result).ToList();
            foreach (var delivery in all)
                Count(delivery);

            all.OfType<InvokeDelivery.Moved>()
                .Where(m => holder is not null && m.Id == holder.Id)
                .Should()
                .HaveCount(
                    deliverable ? 1 : 0,
                    "however many hosts deliver one completion at once, it is applied once"
                );

            // The sweep applies every other ended run as well.
            foreach (var instance in others)
                Apply(instance, RunOf(instance.Token!));
        }

        private async Task OperatorCancelInstance(Instance instance, ClusterHost host)
        {
            MachineInstanceCancelResult result;
            using (var scope = host.Services.CreateScope())
                result = await scope
                    .ServiceProvider.GetRequiredService<IOperationsService>()
                    .CancelMachineInstanceAsync(
                        new MachineInstanceKey(
                            instance.Machine,
                            instance.System ? SnapshotOwnerKind.System : SnapshotOwnerKind.User,
                            instance.Id
                        ),
                        CancellationToken.None
                    );

            if (!instance.System)
            {
                result.Outcome.Should().Be(MachineInstanceCancelOutcome.UserOwned);
                return;
            }

            if (instance.State is null)
            {
                result.Outcome.Should().Be(MachineInstanceCancelOutcome.NotFound);
                return;
            }

            if (instance.Token is null)
            {
                result.Outcome.Should().Be(MachineInstanceCancelOutcome.NoLiveRun);
                return;
            }

            var run = RunOf(instance.Token);
            switch (run.Phase)
            {
                case Phase.Queued:
                    run.Phase = Phase.Withdrawn;
                    if (host == test._full)
                    {
                        result.Outcome.Should().Be(MachineInstanceCancelOutcome.Moved);
                        Apply(instance, run);
                    }
                    else
                        result.Outcome.Should().Be(MachineInstanceCancelOutcome.RunCancelled);
                    break;
                case Phase.Dispatched:
                    result.Outcome.Should().Be(MachineInstanceCancelOutcome.CancelRequested);
                    run.CancelRequested = true;
                    break;
                default:
                    result.Outcome.Should().Be(MachineInstanceCancelOutcome.RunEnded);
                    break;
            }
        }

        private async Task OperatorCancelRun(int pick, ClusterHost host)
        {
            if (Pick(_runs, pick) is not { } run)
                return;

            using var scope = host.Services.CreateScope();
            var operations = scope.ServiceProvider.GetRequiredService<IOperationsService>();
            if (run.MetadataId is { } metadataId)
            {
                await operations.CancelExecutionsAsync([metadataId], CancellationToken.None);
                if (run.Phase == Phase.Dispatched)
                    run.CancelRequested = true;
                return;
            }

            var entry = (await Api.Entry(run.Token))!;
            await operations.CancelWorkQueueEntriesAsync([entry.Id], CancellationToken.None);
            if (run.Phase == Phase.Queued)
                run.Phase = Phase.Withdrawn;
        }

        // The user leaves Running while another host delivers the run's completion: exactly one of them moves it.
        private async Task RaceLeaveAndDeliver(Instance user)
        {
            if (user.State != "Running" || user.Token is null)
                return;

            var run = RunOf(user.Token);
            var deliverable = Ended(run);
            var go = Gate();
            var leave = Task.Run(async () =>
            {
                await go.Task;
                return await Drafts(
                    Api,
                    d => d.Advance(user.UserKey, user.Id, nameof(StepTrigger.Stop))
                );
            });
            var delivery = Task.Run(async () =>
            {
                await go.Task;
                return await Api2.Deliver(run.Token);
            });
            go.SetResult();
            await Task.WhenAll(leave, delivery).WaitAsync(Bound);
            Count(delivery.Result);

            var left = leave.Result is AdvanceOutcome.Advanced;
            var moved = delivery.Result is InvokeDelivery.Moved;
            (left && moved)
                .Should()
                .BeFalse("leaving the state and applying its outcome both moved the instance");
            if (left)
            {
                Move(user, "Idle");
                return;
            }

            deliverable
                .Should()
                .BeTrue(
                    $"the leave failed ({leave.Result}) though nothing else could move the draft"
                );
            moved
                .Should()
                .BeTrue($"neither the leave nor the delivery ({delivery.Result}) moved it");
            Apply(user, run);
        }

        // The user leaves Running while a dispatcher claims the run's entry: either the entry is cancelled and never
        // runs, or it is dispatched and its run's cancel is requested.
        private async Task RaceLeaveAndDispatch(Instance user, ClusterHost host)
        {
            if (user.State != "Running" || user.Token is null)
                return;
            var run = RunOf(user.Token);
            if (run.Phase != Phase.Queued)
                return;

            var go = Gate();
            var leave = Task.Run(async () =>
            {
                await go.Task;
                return await Drafts(
                    Api,
                    d => d.Advance(user.UserKey, user.Id, nameof(StepTrigger.Stop))
                );
            });
            var dispatch = Task.Run(async () =>
            {
                await go.Task;
                using var scope = host.Services.CreateScope();
                await scope
                    .ServiceProvider.GetRequiredService<IJobDispatcherTrain>()
                    .Run(Unit.Default);
            });
            go.SetResult();
            await Task.WhenAll(leave, dispatch).WaitAsync(Bound);

            leave.Result.Should().BeOfType<AdvanceOutcome.Advanced>();
            await ObserveDispatch(host);
            Move(user, "Idle");
        }

        // The user leaves Running while the run's host runs it to its end (and, on the full host, delivers it by
        // its hook): the run ends however it ends, and either the leave or the outcome moves the draft, never both.
        private async Task RaceLeaveAndRun(Instance user)
        {
            if (user.State != "Running" || user.Token is null)
                return;
            var run = RunOf(user.Token);
            if (run.Phase != Phase.Dispatched)
                return;
            var host = run.DispatchedBy!;

            var go = Gate();
            var leave = Task.Run(async () =>
            {
                await go.Task;
                return await Drafts(
                    Api,
                    d => d.Advance(user.UserKey, user.Id, nameof(StepTrigger.Stop))
                );
            });
            var job = Task.Run(async () =>
            {
                await go.Task;
                await host.RunJob(run.MetadataId!.Value);
            });
            go.SetResult();
            await Task.WhenAll(leave, job).WaitAsync(Bound);

            var ended = (await host.Run(run.MetadataId!.Value)).TrainState;
            var own = run.Mode == InvokedStepModes.Fail ? TrainState.Failed : TrainState.Completed;
            ended
                .Should()
                .BeOneOf(
                    run.CancelRequested ? [TrainState.Cancelled] : [own, TrainState.Cancelled],
                    "the run ends its own way, or cancelled by the leave"
                );
            run.Phase = Phase.Ended;
            run.End = ended;

            if (leave.Result is AdvanceOutcome.Advanced)
            {
                Move(user, "Idle");
                return;
            }

            host.Should()
                .Be(
                    test._full,
                    $"the leave failed ({leave.Result}) though only a hook on the full host could move the draft"
                );
            Apply(user, run);
        }

        // Two hosts start one system instance at once: one creates it and queues its run; the other finds it.
        private async Task SystemStartConcurrently(Instance system, string mode)
        {
            var note = $"n{_step}";
            var go = Gate();
            var starts = new[] { Api, Api2 }
                .Select(host =>
                    Task.Run(async () =>
                    {
                        await go.Task;
                        using var scope = host.Services.CreateScope();
                        return await scope
                            .ServiceProvider.GetRequiredService<IMachineInstances>()
                            .Start<SystemStepMachine>(
                                MachineKey.Of(_keyPrefix, system.Name),
                                StepMachine.Context(mode, note)
                            );
                    })
                )
                .ToArray();
            go.SetResult();
            var started = await Task.WhenAll(starts).WaitAsync(Bound);

            started.Select(s => s.Id).Distinct().Should().ContainSingle("one key, one instance");
            started
                .Count(s => s.Created)
                .Should()
                .Be(system.State is null ? 1 : 0, "exactly one start creates the instance");
            if (system.State is not null)
                return;

            system.Id = started[0].Id;
            system.Mode = mode;
            system.Note = note;
            Enter(system, "Running");
        }

        // The draft has been idle past the expiring host's time-to-live, and a load there deletes it: a run it
        // holds is cancelled first, so expiry never strands one.
        private async Task ExpireDraft(Instance user)
        {
            using (var scope = Api.Services.CreateScope())
            {
                var aged = DateTimeOffset.UtcNow - TimeSpan.FromHours(2);
                await scope
                    .ServiceProvider.GetRequiredService<IDataContext>()
                    .SnapshotDrafts.Where(x => x.Id == user.Id && x.Machine == user.Machine)
                    .ExecuteUpdateAsync(s => s.SetProperty(x => x.UpdatedAt, aged));
            }

            var loaded = await Drafts(test._expiring, d => d.Load(user.UserKey, user.Id));

            loaded.Should().BeOfType<LoadResult.NotFound>("the draft expired");
            if (user.State is null)
                return;
            Move(user, "Idle");
            user.State = null;
            user.Mode = "";
            user.Note = "";
            user.Artifact = "";
        }

        private static TaskCompletionSource Gate() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        #endregion

        #region Model transitions

        // A user's declared transition from user.State to target: leaving an invoking state cancels its run,
        // entering one queues a new run.
        private void Move(Instance instance, string target)
        {
            if (instance.Token is { } leaving)
            {
                var run = RunOf(leaving);
                if (run.Phase == Phase.Queued)
                    run.Phase = Phase.Withdrawn;
                else if (run.Phase == Phase.Dispatched)
                    run.CancelRequested = true;
                instance.Token = null;
            }

            if (target is "Running" or "Chained")
                Enter(instance, target);
            else
                instance.State = target;
        }

        private static void Enter(Instance instance, string state)
        {
            instance.State = state;
            instance.Token = null;
            instance.Entering =
                state == "Running"
                    ? (instance.Mode, instance.Note)
                    : (InvokedStepModes.Ok, "next:" + instance.Note);
        }

        // Applies run's outcome to the instance holding its token, as the hook or a delivery does.
        private void ApplyIfHeld(Run run)
        {
            if (Holder(run) is { } holder)
                Apply(holder, run);
        }

        private void Apply(Instance instance, Run run)
        {
            instance.Token.Should().Be(run.Token);
            _applied
                .Add(run.Token)
                .Should()
                .BeTrue($"the model applies {run} once, so the system must too");
            instance.Token = null;

            var end = run.Phase == Phase.Withdrawn ? TrainState.Cancelled : run.End!.Value;
            switch (end)
            {
                case TrainState.Failed:
                    instance.State = "Failed";
                    break;
                case TrainState.Cancelled:
                    instance.State = "Cancelled";
                    break;
                default:
                    switch (instance.State, run.Mode)
                    {
                        case (_, InvokedStepModes.Ok):
                            instance.State = "Done";
                            instance.Artifact = "artifact:" + run.Note;
                            break;
                        case ("Running", InvokedStepModes.Chain):
                            if (instance.System)
                                Enter(instance, "Chained");
                            else
                                instance.State = "Done";
                            break;
                        default:
                            // No OnDone guard accepts the output: the state's failure.
                            instance.State = "Failed";
                            break;
                    }
                    break;
            }
        }

        #endregion

        #region Observation and checks

        // Each step that entered an invoking state queued one run, whose token the row now holds.
        private async Task Adopt()
        {
            foreach (var instance in Instances.Where(i => i.Entering is not null))
            {
                var row = (await Api.Row(instance.Id, instance.Machine))!;
                row.InvokeToken.Should()
                    .NotBeNull($"{instance.Name} entered {instance.State}, which queues a run");
                _runs
                    .Select(r => r.Token)
                    .Should()
                    .NotContain(row.InvokeToken, "entering a state mints a new token");
                var (mode, note) = instance.Entering!.Value;
                _runs.Add(new Run(row.InvokeToken!, instance, mode, note));
                instance.Token = row.InvokeToken;
                instance.Entering = null;
            }
        }

        private async Task ObserveDispatch(ClusterHost host)
        {
            foreach (var run in _runs.Where(r => r.Phase == Phase.Queued))
            {
                var entry = (await Api.Entry(run.Token))!;
                if (entry.Status == WorkQueueStatus.Dispatched)
                {
                    run.Phase = Phase.Dispatched;
                    run.MetadataId = entry.MetadataId;
                    run.DispatchedBy = host;
                }
                else if (entry.Status == WorkQueueStatus.Cancelled)
                    run.Phase = Phase.Withdrawn;
            }
        }

        private async Task Check()
        {
            using var scope = Api.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<IDataContext>();
            var rows = await db.SnapshotDrafts.AsNoTracking().ToListAsync();
            var entries = await db
                .WorkQueues.AsNoTracking()
                .Where(w => w.InvokingInstanceId != null)
                .ToListAsync();
            var metadata = await db
                .Metadatas.AsNoTracking()
                .Where(m => m.InvokingInstanceId != null)
                .ToDictionaryAsync(m => m.Id);
            var machines = Api
                .Services.GetServices<IMachine>()
                .ToDictionary(m => m.Name, m => (IMachineInternals)m);

            // Every stored snapshot rehydrates, in the state the model says.
            rows.Should().HaveCount(Instances.Count(i => i.State is not null));
            foreach (var instance in Instances.Where(i => i.State is not null))
            {
                var row = rows.SingleOrDefault(r =>
                    r.Id == instance.Id && r.Machine == instance.Machine
                );
                row.Should().NotBeNull($"{instance.Name} exists");
                var json = new JsonObject
                {
                    ["machine"] = row!.Machine,
                    ["version"] = row.Version,
                    ["state"] = row.State,
                    ["context"] = JsonNode.Parse(row.Context),
                }.ToJsonString();
                machines[row.Machine]
                    .Rehydrate(json)
                    .Should()
                    .BeOfType<RehydrationResult.Ok>($"{instance.Name}'s stored snapshot is valid")
                    .Which.Snapshot.State.Should()
                    .Be(row.State);

                row.State.Should().Be(instance.State, $"{instance.Name} is where the model is");
                row.InvokeToken.Should().Be(instance.Token, $"{instance.Name}'s token");
                var context = JsonNode.Parse(row.Context)!.AsObject();
                context["artifact"]!
                    .GetValue<string>()
                    .Should()
                    .Be(instance.Artifact, $"{instance.Name}'s outcome was reduced once");
                context["note"]!.GetValue<string>().Should().Be(instance.Note);
                row.OwnerKind.Should()
                    .Be(instance.System ? SnapshotOwnerKind.System : SnapshotOwnerKind.User);
            }

            // Every run the model queued, and no other, with the status the model says.
            entries
                .Select(e => e.ExternalId)
                .Should()
                .BeEquivalentTo(
                    _runs.Select(r => r.Token),
                    "each entry into an invoking state queues one run, and nothing else queues one"
                );
            foreach (var run in _runs)
            {
                var entry = entries.Single(e => e.ExternalId == run.Token);
                entry.InvokingInstanceId.Should().Be(run.Owner.Id);
                entry
                    .Status.Should()
                    .Be(
                        run.Phase switch
                        {
                            Phase.Queued => WorkQueueStatus.Queued,
                            Phase.Withdrawn => WorkQueueStatus.Cancelled,
                            _ => WorkQueueStatus.Dispatched,
                        },
                        $"the entry of {run}"
                    );
                if (run.MetadataId is not { } id)
                    continue;
                entry.MetadataId.Should().Be(id);
                var meta = metadata[id];
                meta.ExternalId.Trim().Should().Be(run.Token);
                meta.TrainState.Should()
                    .Be(
                        run.Phase == Phase.Ended ? run.End!.Value : TrainState.Pending,
                        $"the run of {run}"
                    );
                if (run.Phase == Phase.Dispatched)
                    meta.CancellationRequested.Should()
                        .Be(run.CancelRequested, $"the cancel flag of {run}");
            }

            // At most one live run per instance, the one its token names; no live run is an orphan.
            var tokens = rows.Where(r => r.InvokeToken is not null)
                .Select(r => r.InvokeToken!)
                .ToList();
            tokens.Should().OnlyHaveUniqueItems();
            foreach (var entry in entries)
            {
                var meta = entry.MetadataId is { } id ? metadata[id] : null;
                var live =
                    entry.Status == WorkQueueStatus.Queued
                    || meta?.TrainState is TrainState.Pending or TrainState.InProgress;
                if (!live || meta?.CancellationRequested == true)
                    continue;

                var holder = rows.SingleOrDefault(r => r.InvokeToken == entry.ExternalId);
                holder
                    .Should()
                    .NotBeNull(
                        $"the live run {entry.ExternalId} is no instance's token and its cancel was not "
                            + "requested: it is orphaned"
                    );
                holder!.Id.Should().Be(entry.InvokingInstanceId!.Value);
            }

            foreach (var row in rows.Where(r => r.InvokeToken is not null))
                entries
                    .Should()
                    .Contain(
                        e => e.ExternalId == row.InvokeToken && e.InvokingInstanceId == row.Id,
                        "a token names a run its instance queued"
                    );

            // No run moved an instance twice.
            _moved.Where(m => m.Value > 1).Should().BeEmpty("an outcome is applied at most once");

            await CheckInvariants(machines.Values);
        }

        // The database's own invariants, read from the rows rather than from the model, so a token the model and
        // the system agree on wrongly still fails. Between steps a run may be in progress and an effect claimed,
        // so only the invariants that hold at every moment are checked here. TraxInvariants reads only Postgres.
        private async Task CheckInvariants(IEnumerable<IMachineInternals> machines)
        {
            if (test._cluster.Store != ClusterStore.Postgres)
                return;

            var invoking = machines
                .SelectMany(m => m.InvokedTrains)
                .Select(i => new InvokingState(i.Machine, i.State));
            var violations = (
                await TraxInvariants.FindViolationsAsync(test._cluster.ConnectionString, invoking)
            )
                .Where(v => AlwaysHold.Contains(v.Invariant))
                .ToList();

            violations.Should().BeEmpty(TraxInvariants.Describe(violations));
        }

        private void Count(InvokeDelivery? delivery)
        {
            if (delivery is not InvokeDelivery.Moved moved)
                return;
            var token = Instances.FirstOrDefault(i => i.Id == moved.Id)?.Token;
            if (token is null)
                return;
            _moved[token] = _moved.GetValueOrDefault(token) + 1;
        }

        #endregion

        private Run RunOf(string token) => _runs.Single(r => r.Token == token);

        private Instance? Holder(Run run) => Instances.FirstOrDefault(i => i.Token == run.Token);

        private static bool Ended(Run run) => run.Phase is Phase.Ended or Phase.Withdrawn;

        private bool Deliverable(Instance instance) =>
            instance.Token is { } token && Ended(RunOf(token));

        private static Run? Pick(IEnumerable<Run> runs, int pick)
        {
            var list = runs.ToList();
            return list.Count == 0 ? null : list[pick % list.Count];
        }

        private static async Task<T> Drafts<T>(
            ClusterHost host,
            Func<ISnapshotDraftService, Task<T>> call
        )
        {
            using var scope = host.Services.CreateScope();
            return await call(
                scope
                    .ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>()
                    .Service(UserStepMachine.MachineId)!
            );
        }
    }

    /// <summary>A step that broke the model, naming the step and every run.</summary>
    private sealed class StepFailed(string message, Exception inner) : Exception(message, inner);

    #endregion
}
