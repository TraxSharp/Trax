using AwesomeAssertions;
using CsCheck;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Testing;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;
using Trax.Scheduler.Tests.Integration.Fakes.Timers;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests.Timers;

/// <summary>
/// A model-based property over a machine with a timer, the sibling of <c>InvokesModelTests</c>: generated sequences
/// of client events, due times moved into the past, sweeps on one host or two at once, client events and
/// <c>Extend</c> racing a fire, forged timer triggers and autosaves, expiry, delete and re-create, a live effect
/// claim, and a host running a second version of the machine. Every step drives the real draft service, effect
/// runner and reconciler over one database and is checked against a small model of each draft's state, revision,
/// row id, armed timer and definition. After every step: <c>due_at</c> is set exactly when a timer is armed; the
/// stored revision is the model's; the row id changes only on re-create; a forged timer trigger or autosave changes
/// nothing; each arming fires at most once; no fire happens under a live claim; and the timer invariants of
/// <see cref="TraxInvariants"/> hold. See
/// <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c> and
/// <c>Trax.Docs/adr/0044-property-tests-use-cscheck-in-test-projects-only.md</c>.
/// </summary>
/// <remarks>
/// A failure prints the seed CsCheck shrank it to and the operations. A step that races two hosts is not replayed
/// in the same interleaving by its seed.
/// </remarks>
[TestFixture(ClusterStore.Postgres)]
[TestFixture(ClusterStore.Sqlite)]
[NonParallelizable]
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
public class TimersModelTests(ClusterStore store)
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const int Iterations = 100;

    private const int MinSteps = 4;

    private const int MaxSteps = 30;

    // The name a client would have to send to fire the reminder's timer through an advance.
    private const string TimerTrigger = "Review.after";

    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    // The TraxInvariants on timers that hold between any two steps.
    private static readonly HashSet<string> AlwaysHold =
    [
        TraxInvariants.RevisionBelowOne,
        TraxInvariants.DueAtOutsideTimedState,
        TraxInvariants.TimedStateWithoutDueAt,
        TraxInvariants.DuplicateRowId,
    ];

    private static readonly TimedState[] TimedStates =
    [
        new(ReminderMachine.MachineId, nameof(ReminderState.Review)),
    ];

    private readonly PauseGate _gate = new();

    private InvokeCluster _cluster = null!;
    private TimerDb _db = null!;
    private ClusterHost _api = null!;
    private ClusterHost _api2 = null!;
    private ClusterHost _pausing = null!;
    private ClusterHost _expiring = null!;
    private ClusterHost _v2 = null!;

    [OneTimeSetUp]
    public async Task CreateCluster()
    {
        _cluster = await InvokeCluster.Create(store);
        _db = new TimerDb(_cluster);
        _api = _cluster.Host(machines: true, scheduler: false, TimerHosts.Publishing);
        _api2 = _cluster.Host(machines: true, scheduler: false, TimerHosts.Publishing);
        _pausing = _cluster.Host(
            machines: true,
            scheduler: false,
            TimerHosts.All(TimerHosts.Publishing, TimerHosts.Pausing(_gate))
        );
        _expiring = _cluster.Host(
            machines: true,
            scheduler: false,
            TimerHosts.Publishing,
            options: o => o.DraftTtl = TimeSpan.FromHours(1)
        );
        _v2 = _cluster.Host(
            machines: true,
            scheduler: false,
            TimerHosts.All(TimerHosts.Publishing, TimerHosts.Deploy(ReminderShape.V2SameTimer))
        );
    }

    [OneTimeTearDown]
    public async Task DisposeCluster()
    {
        await _api.DisposeAsync();
        await _api2.DisposeAsync();
        await _pausing.DisposeAsync();
        await _expiring.DisposeAsync();
        await _v2.DisposeAsync();
        await _cluster.DisposeAsync();
    }

    [TearDown]
    public void Release()
    {
        _gate.Reset();
        ReminderPublishGate.Reset();
    }

    [Test]
    public Task Generated_events_fires_and_races_keep_due_at_revision_and_row_id_in_step_with_the_model() =>
        AnOperation
            .Array[MinSteps, MaxSteps]
            .SampleAsync(
                async ops =>
                {
                    await _cluster.Reset();
                    _gate.Reset();
                    ReminderPublishGate.Reset();
                    await new World(this).Run(ops);
                },
                iter: Iterations,
                threads: 1,
                print: Describe
            );

    #region Operations

    private enum Kind
    {
        Open,
        Approve,
        Extend,
        MakeDue,
        FireTimers,
        FireTimersConcurrently,
        ClientEventRacingFire,
        ExtendRacingFire,
        ForgeTimerTrigger,
        AutosaveTimedState,
        ExpireDraft,
        DeleteAndRecreate,
        ClaimEffect,
        DeployV2,
    }

    /// <summary>One generated step on the draft of user <c>u{A % 2}</c>; <see cref="B"/> picks a variant.</summary>
    private readonly record struct Op(Kind Kind, int A, int B)
    {
        public override string ToString() => $"{Kind}(u{A % 2}, {B})";
    }

    private static Gen<Op> Of(Kind kind, int b = 0) =>
        Gen.Select(Gen.Int[0, 1], Gen.Int[0, b]).Select(t => new Op(kind, t.Item1, t.Item2));

    // Weighted so that drafts are opened, made due and fired often enough to meet the races.
    private static readonly Gen<Op> AnOperation = Gen.Frequency(
        (8, Of(Kind.Open)),
        (3, Of(Kind.Approve)),
        (4, Of(Kind.Extend)),
        (8, Of(Kind.MakeDue)),
        (6, Of(Kind.FireTimers, 1)),
        (3, Of(Kind.FireTimersConcurrently)),
        (3, Of(Kind.ClientEventRacingFire)),
        (3, Of(Kind.ExtendRacingFire)),
        (2, Of(Kind.ForgeTimerTrigger)),
        (2, Of(Kind.AutosaveTimedState, 1)),
        (2, Of(Kind.ExpireDraft)),
        (2, Of(Kind.DeleteAndRecreate)),
        (2, Of(Kind.ClaimEffect)),
        (2, Of(Kind.DeployV2))
    );

    private static string Describe(Op[] ops) =>
        string.Join(Environment.NewLine, ops.Select((op, i) => $"  {i}: {op}"));

    #endregion

    #region Model

    /// <summary>What one user's reminder should be.</summary>
    private sealed class Draft(string user)
    {
        public string User { get; } = user;

        public Guid Id { get; set; } = Guid.NewGuid();

        /// <summary>Null while no row exists.</summary>
        public string? State { get; set; }

        public long Revision { get; set; }

        public long? RowId { get; set; }

        /// <summary>Whether a timer is armed: the draft is in <c>Review</c> and the timer has not fired or been left.</summary>
        public bool Armed => State == nameof(ReminderState.Review);

        /// <summary>Whether the armed timer's due time has been moved into the past.</summary>
        public bool Due { get; set; }

        /// <summary>The machine version the row was last written by.</summary>
        public int Version { get; set; } = 1;

        /// <summary>How many times the current arming has fired: never more than once.</summary>
        public int FiredThisArming { get; set; }
    }

    private sealed class World(TimersModelTests test)
    {
        private readonly Draft[] _drafts =
        [
            new("u0-" + Guid.NewGuid().ToString("N")),
            new("u1-" + Guid.NewGuid().ToString("N")),
        ];
        private int _requests;

        public async Task Run(Op[] ops)
        {
            for (var i = 0; i < ops.Length; i++)
            {
                try
                {
                    await Apply(ops[i]);
                    await Check();
                }
                catch (Exception ex)
                {
                    throw new StepFailed($"Step {i} ({ops[i]}) broke the model: {ex.Message}", ex);
                }
            }
        }

        private async Task Apply(Op op)
        {
            var draft = _drafts[op.A % 2];
            switch (op.Kind)
            {
                case Kind.Open:
                    await Open(draft);
                    break;
                case Kind.Approve:
                    await Event(draft, ReminderTrigger.Approve, nameof(ReminderState.Done));
                    break;
                case Kind.Extend:
                    await Event(draft, ReminderTrigger.Extend, nameof(ReminderState.Review));
                    break;
                case Kind.MakeDue:
                    if (draft.Armed)
                    {
                        await test._db.MakeDue(draft.RowId!.Value);
                        draft.Revision++;
                        draft.Due = true;
                    }
                    break;
                case Kind.FireTimers:
                    Fired(
                        await (op.B == 0 ? test._api : test._api2).FireTimers(),
                        readsVersion2: false
                    );
                    break;
                case Kind.FireTimersConcurrently:
                    var both = await Task.WhenAll(test._api.FireTimers(), test._api2.FireTimers())
                        .WaitAsync(Bound);
                    Fired(both.SelectMany(b => b).ToList(), readsVersion2: false);
                    break;
                case Kind.ClientEventRacingFire:
                    await RaceFire(draft, ReminderTrigger.Approve);
                    break;
                case Kind.ExtendRacingFire:
                    await RaceFire(draft, ReminderTrigger.Extend);
                    break;
                case Kind.ForgeTimerTrigger:
                    if (draft.State is not null)
                        await Forged(
                            draft,
                            () => Advance(test._api, draft, TimerTrigger),
                            RefusalCode.TimerBound
                        );
                    break;
                case Kind.AutosaveTimedState:
                    if (draft.State is not null)
                    {
                        var target =
                            op.B == 0
                                ? nameof(ReminderState.Expired)
                                : nameof(ReminderState.Review);
                        var saved = await test._api.Save(
                            draft.User,
                            draft.Id,
                            ReminderMachine.Json(
                                target,
                                ReminderMachine.Context("rewritten"),
                                draft.Version
                            )
                        );
                        if (draft.Armed)
                            saved
                                .Should()
                                .NotBeOfType<AutosaveResult.Saved>(
                                    $"autosave never leaves or rewrites a timed state. See {Adr}"
                                );
                        else if (saved is AutosaveResult.Saved)
                        {
                            draft.State = target;
                            draft.Revision++;
                            draft.Due = false;
                            draft.FiredThisArming = 0;
                        }
                    }
                    break;
                case Kind.ExpireDraft:
                    if (draft.State is not null)
                    {
                        await test._api.IdleDraft(draft.RowId!.Value, TimeSpan.FromHours(2));
                        draft.Revision++;
                        await test._expiring.ExpireDrafts();
                        var loaded = await Load(test._expiring, draft);
                        if (draft.Armed)
                            loaded
                                .Should()
                                .BeOfType<LoadResult.Loaded>("a pending timer keeps the draft");
                        else if (draft.State == nameof(ReminderState.Published))
                            loaded
                                .Should()
                                .BeOfType<LoadResult.Loaded>(
                                    "the effect's target is the record of what the server did, which the TTL keeps"
                                );
                        else
                        {
                            loaded.Should().BeOfType<LoadResult.NotFound>("idle past its TTL");
                            Forget(draft);
                        }
                    }
                    break;
                case Kind.DeleteAndRecreate:
                    // Published is the effect's target, which discard refuses (discard-not-allowed): only the server
                    // puts a draft there, so its owner cannot erase it.
                    if (draft.State is not null and not nameof(ReminderState.Published))
                    {
                        await test._api.Delete(draft.User, draft.Id);
                        var oldRow = draft.RowId;
                        Forget(draft);
                        await Open(draft);
                        draft.RowId.Should().NotBe(oldRow, "a re-created draft has a new row id");
                    }
                    break;
                case Kind.ClaimEffect:
                    await Claim(draft);
                    break;
                case Kind.DeployV2:
                    Fired(await test._v2.FireTimers(), readsVersion2: true);
                    break;
            }
        }

        private async Task Open(Draft draft)
        {
            if (draft.State is not null)
            {
                (await Advance(test._api, draft, nameof(ReminderTrigger.Open)))
                    .Should()
                    .NotBeOfType<AdvanceOutcome.Advanced>("Open has no edge out of " + draft.State);
                return;
            }

            (await test._api.OpenReview(draft.User, draft.Id))
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>();
            draft.State = nameof(ReminderState.Review);
            draft.RowId = (await test._api.Row(draft.Id, ReminderMachine.MachineId))!.RowId;
            draft.Revision = 2;
            draft.Due = false;
            draft.Version = 1;
            draft.FiredThisArming = 0;
        }

        private async Task Event(Draft draft, ReminderTrigger trigger, string target)
        {
            if (draft.State is null)
                return;
            var result = await Advance(test._api, draft, trigger.ToString());
            if (draft.State != nameof(ReminderState.Review))
            {
                result.Should().NotBeOfType<AdvanceOutcome.Advanced>();
                return;
            }

            result.Should().BeOfType<AdvanceOutcome.Advanced>();
            draft.State = target;
            draft.Revision++;
            draft.Due = false;
            draft.FiredThisArming = 0;
        }

        private async Task RaceFire(Draft draft, ReminderTrigger trigger)
        {
            if (!draft.Armed || !draft.Due || draft.Version != 1)
                return;

            test._gate.Arm();
            var held = Advance(test._pausing, draft, trigger.ToString());
            await test._gate.Reached.WaitAsync(Bound);
            Fired(await test._api.FireTimers(), readsVersion2: false);
            test._gate.Release();

            (await held.WaitAsync(Bound))
                .Should()
                .NotBeOfType<AdvanceOutcome.Advanced>(
                    $"the fire won, and the client's event meets Expired. See {Adr}"
                );
        }

        private async Task Claim(Draft draft)
        {
            if (draft.State != nameof(ReminderState.Review) || draft.Version != 1)
                return;

            ReminderPublishGate.Reset();
            ReminderPublishGate.Armed = true;
            var publish = test._api.Publish(draft.User, draft.Id, NextRequest());
            await ReminderPublishGate.Entered.WaitAsync(Bound);
            var underClaim = await test._api2.FireTimers();
            underClaim
                .Should()
                .NotContain(
                    r => r.InstanceId == draft.Id && r.Outcome == TimerFireResult.Fired,
                    $"no fire under a live effect claim. See {Adr}"
                );
            // The sweep fires every other draft that is due, which the model applies as any sweep's.
            Fired(underClaim, readsVersion2: false, waiting: draft);
            ReminderPublishGate.Release();
            (await publish.WaitAsync(Bound)).Should().BeOfType<AdvanceOutcome.Advanced>();
            ReminderPublishGate.Reset();

            // The send marks the row, then records the effect: two writes.
            draft.State = nameof(ReminderState.Published);
            draft.Revision += 2;
            draft.Due = false;
            draft.FiredThisArming = 0;
        }

        // Applies to the model every fire a sweep reported, and checks it fired only what was due and readable.
        private void Fired(
            IReadOnlyList<TimerFireResult> results,
            bool readsVersion2,
            Draft? waiting = null
        )
        {
            foreach (var draft in _drafts.Where(d => d.State is not null && d != waiting))
            {
                var fired = results.Count(r =>
                    r.InstanceId == draft.Id && r.Outcome == TimerFireResult.Fired
                );
                var expected = draft.Armed && draft.Due && (readsVersion2 || draft.Version == 1);
                fired.Should().Be(expected ? 1 : 0, $"{draft.User}'s timer fires once when due");
                if (fired == 0)
                    continue;

                draft.FiredThisArming++;
                draft.FiredThisArming.Should().Be(1, "each arming fires at most once");
                draft.State = nameof(ReminderState.Expired);
                draft.Revision++;
                draft.Due = false;
                if (readsVersion2)
                    draft.Version = 2;
            }
        }

        private async Task Forged(Draft draft, Func<Task<AdvanceOutcome>> forge, RefusalCode reason)
        {
            var revision = draft.Revision;
            (await forge())
                .Should()
                .BeOfType<AdvanceOutcome.Rejected>(
                    $"a client can never fire a timer trigger. See {Adr}"
                )
                .Which.Reason.Should()
                .Be(reason);
            draft.Revision.Should().Be(revision);
        }

        private async Task<AdvanceOutcome> Advance(ClusterHost host, Draft draft, string trigger)
        {
            using var scope = host.Services.CreateScope();
            return await scope
                .ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>()
                .Service(ReminderMachine.MachineId)!
                .Advance(draft.User, draft.Id, trigger, requestId: NextRequest());
        }

        private static Task<LoadResult> Load(ClusterHost host, Draft draft) =>
            host.Load(draft.User, draft.Id);

        private static void Forget(Draft draft)
        {
            draft.State = null;
            draft.RowId = null;
            draft.Revision = 0;
            draft.Due = false;
            draft.Version = 1;
            draft.FiredThisArming = 0;
        }

        private string NextRequest() => "req-" + Interlocked.Increment(ref _requests);

        #region Checks

        private async Task Check()
        {
            foreach (var draft in _drafts)
            {
                var rows = await test._api.Rows(draft.Id, ReminderMachine.MachineId);
                if (draft.State is null)
                {
                    rows.Should().BeEmpty($"{draft.User} has no draft");
                    continue;
                }

                var row = rows.Should().ContainSingle().Subject;
                row.State.Should().Be(draft.State);
                row.RowId.Should().Be(draft.RowId!.Value, "the row id changes only on re-create");

                (await test._db.DueAt(row.RowId) is not null)
                    .Should()
                    .Be(draft.Armed, $"due_at is set exactly when a timer is armed. See {Adr}");
                (await test._db.Revision(row.RowId))
                    .Should()
                    .Be(draft.Revision, "every write bumps the revision by one");
            }

            await CheckInvariants();
        }

        private async Task CheckInvariants()
        {
            IReadOnlyList<TraxInvariantViolation> found;
            if (test._cluster.Store == ClusterStore.Postgres)
                found = await TraxInvariants.FindViolationsAsync(
                    test._cluster.ConnectionString,
                    [],
                    TimedStates
                );
            else
            {
                await using var connection = new SqliteConnection(test._cluster.ConnectionString);
                await connection.OpenAsync();
                found = await TraxInvariants.FindViolationsAsync(connection, [], TimedStates);
            }

            var violations = found.Where(v => AlwaysHold.Contains(v.Invariant)).ToList();
            violations.Should().BeEmpty(TraxInvariants.Describe(violations));
        }

        #endregion
    }

    /// <summary>A step that broke the model, naming the step.</summary>
    private sealed class StepFailed(string message, Exception inner) : Exception(message, inner);

    #endregion
}
