using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Extensions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Testing;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.StateMachine.Persistence.Integration.Fakes;
using Trax.StateMachine.Persistence.Integration.Fixtures;
using SnapshotTransition = Trax.Effect.Models.SnapshotTransition.SnapshotTransition;

namespace Trax.StateMachine.Persistence.Integration;

/// <summary>
/// Every write of an instance's snapshot adds exactly one row to its history, with the revision that write gave it,
/// what wrote it, the command, who made it and the states it moved between, in the same transaction as the write. A
/// write that leaves the snapshot alone, and a write that is refused or loses its race, adds none, and the history
/// goes with the instance however it is removed. Each per-site test makes one write and reads the history that
/// changed. Enforces <c>Trax.StateMachine/docs/adr/0003-a-machine-instance-history-is-written-with-its-snapshot.md</c>.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
[Property(
    "adr",
    "Trax.StateMachine/docs/adr/0003-a-machine-instance-history-is-written-with-its-snapshot.md"
)]
public class TransitionHistoryTests(StoreProvider provider)
{
    private const string Adr =
        "Trax.StateMachine/docs/adr/0003-a-machine-instance-history-is-written-with-its-snapshot.md";

    private const string User = "history-user";

    private InstanceHost? _host;

    [SetUp]
    public void SetUp() => _host = InstanceHost.Create(provider, launcher: new RecordingLauncher());

    [TearDown]
    public void TearDown() => _host?.Dispose();

    private static JsonObject Source => new() { ["source"] = "repo" };

    private static JsonObject Quarter => new() { ["coin"] = "quarter" };

    private static MachineKey NewKey() => MachineKey.Of("history", Guid.NewGuid().ToString());

    private static Snapshot Turnstile(string state, JsonObject? context = null) =>
        new()
        {
            Machine = "turnstile",
            Version = 1,
            State = state,
            Context = context ?? new JsonObject(),
        };

    private static Snapshot UnlockedTurnstile =>
        Turnstile("Unlocked", new JsonObject { ["paidWith"] = "quarter" });

    /// <summary>The history of the row <paramref name="kind"/> holds under <paramref name="id"/>, oldest first.</summary>
    private Task<List<SnapshotTransition>> History(
        Guid id,
        SnapshotOwnerKind kind = SnapshotOwnerKind.User
    ) => HistoryOf(_host.NotNull(), id, kind);

    private static async Task<List<SnapshotTransition>> HistoryOf(
        InstanceHost host,
        Guid id,
        SnapshotOwnerKind kind = SnapshotOwnerKind.User
    )
    {
        var rowId = await host.RowId(id, kind).ConfigureAwait(false);
        return await RowHistory(host, rowId).ConfigureAwait(false);
    }

    private static async Task<List<SnapshotTransition>> RowHistory(InstanceHost host, long rowId)
    {
        using var scope = host.Scope();
        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .SnapshotTransitions.AsNoTracking()
            .Where(t => t.DraftRowId == rowId)
            .OrderBy(t => t.Revision)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    // Makes one write and returns the one row of history it added, asserting it is exactly one, at the revision the
    // instance now holds.
    private async Task<SnapshotTransition> OneRowFor(
        Guid id,
        Func<Task> write,
        SnapshotOwnerKind kind = SnapshotOwnerKind.User
    )
    {
        var before = await History(id, kind).ConfigureAwait(false);
        await write().ConfigureAwait(false);
        var after = await History(id, kind).ConfigureAwait(false);
        var added = after.Skip(before.Count).ToList();
        added.Should().ContainSingle($"one snapshot write adds one row of history. See {Adr}");
        after.Take(before.Count).Should().BeEquivalentTo(before, "history is only appended to");
        added[0]
            .Revision.Should()
            .Be(
                await _host.NotNull().Revision(id, kind).ConfigureAwait(false),
                "the row carries the revision the write gave"
            );
        return added[0];
    }

    // A turnstile's draft, written through the store as host code does: the generic save does not serve a machine
    // with a guard.
    private async Task<Guid> Seeded(string json)
    {
        var id = Guid.NewGuid();
        await _host.NotNull().SeedDraft(User, id, json).ConfigureAwait(false);
        return id;
    }

    private async Task<Guid> Saved(string machine, string json)
    {
        var id = Guid.NewGuid();
        using var scope = _host.NotNull().Scope();
        (
            await _host
                .NotNull()
                .Service(scope, machine)
                .Autosave(User, id, json)
                .ConfigureAwait(false)
        )
            .Should()
            .BeOfType<AutosaveResult.Saved>();
        return id;
    }

    private async Task<AdvanceOutcome> Advance(
        string machine,
        Guid id,
        string trigger,
        JsonNode? input = null,
        string? requestId = null
    )
    {
        using var scope = _host.NotNull().Scope();
        return await _host
            .NotNull()
            .Service(scope, machine)
            .Advance(User, id, trigger, input, requestId)
            .ConfigureAwait(false);
    }

    private async Task<Guid> DraftInBuilding()
    {
        var id = Guid.NewGuid();
        await _host
            .NotNull()
            .SeedDraft(User, id, BuildMachine.Json("Draft", Source))
            .ConfigureAwait(false);
        (await Advance(BuildMachine.Id, id, "Build").ConfigureAwait(false))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Which.Snapshot.State.Should()
            .Be("Building");
        return id;
    }

    private async Task CancelEntry(string token)
    {
        using var scope = _host.NotNull().Scope();
        await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .WorkQueues.Where(w => w.ExternalId == token)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, WorkQueueStatus.Cancelled))
            .ConfigureAwait(false);
    }

    #region One test per write site

    [Test]
    public async Task Every_creation_starts_the_history_at_revision_one()
    {
        var saved = await Saved(PlainHatchMachine.Id, PlainHatchMachine.Json(HatchState.Shut));
        var system = (await _host.NotNull().Start<SystemTurnstileMachine>(NewKey())).Id;
        StartOutcome started;
        using (var scope = _host.NotNull().Scope())
            started = await _host
                .NotNull()
                .Service(scope, StartableMachine.Id)
                .Start(
                    new Actor.Client(User),
                    new JsonObject { ["title"] = "t", ["copies"] = 1 },
                    requestId: $"start-{Guid.NewGuid():N}"
                );
        var start = started.Should().BeOfType<StartOutcome.Started>().Subject;

        foreach (
            var (history, actor, label) in new[]
            {
                (await History(saved), SnapshotTransitionActor.Owner, "an autosave's insert"),
                (
                    await History(system, SnapshotOwnerKind.System),
                    SnapshotTransitionActor.Host,
                    "a system start"
                ),
                (await History(start.Id), SnapshotTransitionActor.Owner, "a client's start"),
            }
        )
        {
            var row = history.Should().ContainSingle($"{label} writes one row. See {Adr}").Subject;
            row.Revision.Should().Be(1, $"{label} is the instance's first revision");
            row.Kind.Should().Be(SnapshotTransitionKind.Start);
            row.Actor.Should().Be(actor, label);
            row.FromState.Should().BeNull("a creation leaves no state");
            row.Trigger.Should().BeNull();
        }
    }

    [TestCase("upsert")]
    [TestCase("update-with-request")]
    public async Task An_autosave_adds_a_save(string write)
    {
        var (machine, first, second) = string.Equals(write, "upsert", StringComparison.Ordinal)
            ? (
                PlainHatchMachine.Id,
                PlainHatchMachine.Json(HatchState.Shut),
                PlainHatchMachine.Json(HatchState.Open)
            )
            : (
                LedgerMachine.Id,
                LedgerMachine.Json(LedgerState.Open),
                LedgerMachine.Json(LedgerState.Counting, 1, 2)
            );
        var id = await Saved(machine, first);
        // The guarded save keeps the request the row recorded; that must not make the save look like its command.
        if (!string.Equals(write, "upsert", StringComparison.Ordinal))
            (await Advance(machine, id, nameof(LedgerTrigger.Count), requestId: "r1"))
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>();
        var stateBefore = (await _host.NotNull().Rows(id)).Single().State;

        var row = await OneRowFor(
            id,
            async () =>
            {
                using var scope = _host.NotNull().Scope();
                (
                    await _host
                        .NotNull()
                        .Service(scope, machine)
                        .Autosave(User, id, second)
                        .ConfigureAwait(false)
                )
                    .Should()
                    .BeOfType<AutosaveResult.Saved>();
            }
        );

        row.Kind.Should().Be(SnapshotTransitionKind.Save, $"an autosave is a save. See {Adr}");
        row.Trigger.Should().BeNull("a save sends no command");
        row.Actor.Should().Be(SnapshotTransitionActor.Owner);
        row.FromState.Should().Be(stateBefore);
    }

    [Test]
    public async Task An_advance_adds_its_command()
    {
        var id = await Seeded(TestTurnstile.InitialJson);

        var row = await OneRowFor(
            id,
            async () =>
                (await Advance("turnstile", id, "Coin", Quarter).ConfigureAwait(false))
                    .Should()
                    .BeOfType<AdvanceOutcome.Advanced>()
        );

        row.Kind.Should().Be(SnapshotTransitionKind.Command);
        row.Trigger.Should().Be("Coin", $"the command that moved it is recorded. See {Adr}");
        row.Actor.Should().Be(SnapshotTransitionActor.Owner);
        row.FromState.Should().Be("Locked");
        row.ToState.Should().Be("Unlocked");
        row.InvokeToken.Should().BeNull("the turnstile starts no run");
        row.At.Should()
            .Be(
                (await _host.NotNull().Rows(id)).Single().UpdatedAt,
                "the row's time is the write's updated_at"
            );
    }

    [Test]
    public async Task Entering_an_invoking_state_records_the_run_it_started()
    {
        var id = Guid.NewGuid();
        await _host.NotNull().SeedDraft(User, id, BuildMachine.Json("Draft", Source));

        var row = await OneRowFor(
            id,
            async () =>
                (await Advance(BuildMachine.Id, id, "Build").ConfigureAwait(false))
                    .Should()
                    .BeOfType<AdvanceOutcome.Advanced>()
        );

        row.Trigger.Should().Be("Build");
        row.ToState.Should().Be("Building");
        row.InvokeToken.Should()
            .NotBeNull()
            .And.Be(
                (await _host.NotNull().Rows(id)).Single().InvokeToken,
                $"the history names the run entering the state queued. See {Adr}"
            );
    }

    [Test]
    public async Task Leaving_an_invoking_state_adds_one_row()
    {
        var id = await DraftInBuilding();

        var row = await OneRowFor(
            id,
            async () =>
                (await Advance(BuildMachine.Id, id, "StopBuild").ConfigureAwait(false))
                    .Should()
                    .BeOfType<AdvanceOutcome.Advanced>()
        );

        row.Trigger.Should().Be("StopBuild");
        row.FromState.Should().Be("Building");
        row.ToState.Should().Be("Draft");
        row.InvokeToken.Should().BeNull("leaving starts nothing");
    }

    [Test]
    public async Task A_send_adds_one_row_for_the_record_and_none_for_the_mark()
    {
        var id = Guid.NewGuid();
        await _host.NotNull().SeedDraft(User, id, OrderMachine.ReviewSnapshot(1, 2));
        var r0 = await _host.NotNull().Revision(id);

        var row = await OneRowFor(
            id,
            async () =>
            {
                using var scope = _host.NotNull().Scope();
                (
                    await scope
                        .ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>()
                        .EffectRunner("order")
                        .NotNull()
                        .Run(User, id, "send-1")
                        .ConfigureAwait(false)
                )
                    .Should()
                    .BeOfType<AdvanceOutcome.Advanced>();
            }
        );

        row.Revision.Should()
            .Be(r0 + 2, $"the mark leaves the snapshot alone and adds no row. See {Adr}");
        row.Kind.Should().Be(SnapshotTransitionKind.Command);
        row.Trigger.Should().Be("Place", "the effect's event moved it");
        row.Actor.Should().Be(SnapshotTransitionActor.Owner);
        row.ToState.Should().Be("Placed");
    }

    [Test]
    public async Task A_system_advance_is_the_hosts_command()
    {
        var key = NewKey();
        var id = (await _host.NotNull().Start<SystemTurnstileMachine>(key)).Id;

        var row = await OneRowFor(
            id,
            async () =>
            {
                using var scope = _host.NotNull().Scope();
                (
                    await scope
                        .ServiceProvider.GetRequiredService<IMachineInstances>()
                        .Advance<SystemTurnstileMachine>(key, "Coin", Quarter)
                        .ConfigureAwait(false)
                )
                    .Should()
                    .BeOfType<AdvanceOutcome.Advanced>();
            },
            SnapshotOwnerKind.System
        );

        row.Kind.Should().Be(SnapshotTransitionKind.Command);
        row.Trigger.Should().Be("Coin");
        row.Actor.Should().Be(SnapshotTransitionActor.Host, $"host code drove it. See {Adr}");
    }

    [Test]
    public async Task An_outcome_applied_by_the_hook_is_the_machines()
    {
        var id = await DraftInBuilding();
        var token = (await _host.NotNull().Rows(id)).Single().InvokeToken.NotNull();
        await CancelEntry(token);

        var row = await OneRowFor(
            id,
            async () =>
            {
                using var scope = _host.NotNull().Scope();
                (
                    await scope
                        .ServiceProvider.GetRequiredService<IInvokedRunOutcomes>()
                        .Deliver(token)
                        .ConfigureAwait(false)
                )
                    .Should()
                    .Be("BuildCancelled");
            }
        );

        row.Kind.Should().Be(SnapshotTransitionKind.Outcome);
        row.Trigger.Should().Be("Building.cancelled", $"the outcome applied is named. See {Adr}");
        row.Actor.Should().Be(SnapshotTransitionActor.Machine);
        row.FromState.Should().Be("Building");
        row.ToState.Should().Be("BuildCancelled");
    }

    [Test]
    public async Task An_outcome_applied_by_the_reconciler_adds_one_row()
    {
        var id = await DraftInBuilding();
        await CancelEntry((await _host.NotNull().Rows(id)).Single().InvokeToken.NotNull());

        var row = await OneRowFor(
            id,
            async () =>
            {
                using var scope = _host.NotNull().Scope();
                await scope
                    .ServiceProvider.GetRequiredService<InvokeOutcomeReconciler>()
                    .SweepOnce(CancellationToken.None)
                    .ConfigureAwait(false);
            }
        );

        row.Kind.Should().Be(SnapshotTransitionKind.Outcome);
        row.Trigger.Should().Be("Building.cancelled");
    }

    [Test]
    public async Task A_stranded_outcome_adds_one_row_in_the_same_state()
    {
        var id = await DraftInBuilding();
        var token = (await _host.NotNull().Rows(id)).Single().InvokeToken.NotNull();

        var row = await OneRowFor(
            id,
            async () =>
            {
                using var scope = _host.NotNull().Scope();
                var store = _host.NotNull().Instances(scope);
                var stored = await store
                    .Get(DraftOwner.User(User), BuildMachine.Id, id)
                    .ConfigureAwait(false);
                var building = new Snapshot
                {
                    Machine = BuildMachine.Id,
                    Version = 1,
                    State = "Building",
                    Context = Source,
                };
                (
                    await store
                        .StrandByInvokeToken(token, building, stored.NotNull().Token)
                        .ConfigureAwait(false)
                )
                    .Should()
                    .BeTrue();
            }
        );

        row.Kind.Should().Be(SnapshotTransitionKind.Outcome);
        row.Trigger.Should().BeNull($"no outcome could be applied. See {Adr}");
        row.FromState.Should().Be("Building");
        row.ToState.Should().Be("Building");
    }

    [Test]
    public async Task A_timer_fire_is_the_machines()
    {
        var id = Guid.NewGuid();
        await _host.NotNull().SeedDraft(User, id, ReminderMachine.Json("Idle"));
        using (var scope = _host.NotNull().Scope())
            (
                await _host
                    .NotNull()
                    .Service(scope, ReminderMachine.Id)
                    .Advance(new Actor.Client(User, "Editor"), id, "Open")
            )
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>();
        (
            await RawSql.SetDueAt(
                _host.NotNull(),
                id,
                (await RawSql.Now(_host.NotNull())).AddMinutes(-1)
            )
        )
            .Should()
            .Be(1);

        var row = await OneRowFor(
            id,
            async () =>
                (
                    await _host
                        .NotNull()
                        .Services.GetRequiredService<InvokeOutcomeReconciler>()
                        .FireDueTimersOnce(CancellationToken.None)
                        .ConfigureAwait(false)
                )
                    .Should()
                    .Contain(r => r.InstanceId == id && r.Outcome == TimerFireResult.Fired)
        );

        row.Kind.Should().Be(SnapshotTransitionKind.Timer, $"a timer moved it. See {Adr}");
        row.Trigger.Should().Be(ReminderMachine.ReviewTimer);
        row.Actor.Should().Be(SnapshotTransitionActor.Machine);
        row.FromState.Should().Be(nameof(ReminderState.Review));
        row.ToState.Should().Be(nameof(ReminderState.Expired));
    }

    [Test]
    public async Task A_spawn_starts_the_childs_history_and_names_the_child_on_the_parents()
    {
        var id = Guid.NewGuid();
        await _host.NotNull().Seed(User, id, ConsignmentMachine.At(ConsignmentState.Open));

        var row = await OneRowFor(
            id,
            async () =>
                (await Advance(ConsignmentMachine.Id, id, "Pack").ConfigureAwait(false))
                    .Should()
                    .BeOfType<AdvanceOutcome.Advanced>()
        );

        var parent = (await _host.NotNull().Rows(id)).Single();
        row.InvokeToken.Should()
            .Be(parent.InvokeToken, $"the parent's row names what it spawned. See {Adr}");
        var childId = Guid.Parse(
            JsonNode.Parse(parent.Context).NotNull()["parcelId"].NotNull().GetValue<string>()
        );
        var child = (await History(childId)).Should().ContainSingle().Subject;
        child.Kind.Should().Be(SnapshotTransitionKind.Start);
        child.Actor.Should().Be(SnapshotTransitionActor.Machine, "the parent spawned it");
        child.InvokeToken.Should().BeNull("the child started nothing");
    }

    [TestCase("Update")]
    [TestCase("UpdateWithRequest")]
    [TestCase("Upsert")]
    public async Task A_public_ISnapshotStore_write_adds_one_row(string write)
    {
        var id = await Seeded(TestTurnstile.InitialJson);

        var row = await OneRowFor(
            id,
            async () =>
            {
                using var scope = _host.NotNull().Scope();
                var store = scope.ServiceProvider.GetRequiredService<ISnapshotStore>();
                var token = (await store.Get(User, "turnstile", id).ConfigureAwait(false))
                    .NotNull()
                    .Token;
                var wrote = write switch
                {
                    "Update" => await store
                        .Update(User, id, UnlockedTurnstile, token)
                        .ConfigureAwait(false),
                    "UpdateWithRequest" => await store
                        .UpdateWithRequest(
                            User,
                            id,
                            UnlockedTurnstile,
                            token,
                            new AppliedRequest("r1", "Coin", "Locked")
                        )
                        .ConfigureAwait(false),
                    _ => await store.Upsert(User, id, UnlockedTurnstile).ConfigureAwait(false),
                };
                wrote.Should().BeTrue();
            }
        );

        row.Kind.Should()
            .Be(
                string.Equals(write, "UpdateWithRequest", StringComparison.Ordinal)
                    ? SnapshotTransitionKind.Command
                    : SnapshotTransitionKind.Save,
                $"host code writing through the store is a save, or the command its request names. See {Adr}"
            );
        row.Actor.Should().Be(SnapshotTransitionActor.Owner);
    }

    #endregion

    #region Writes that add nothing

    [Test]
    [LeavesStuckRuns(
        "writes an invoke token straight into the store, with no queued run behind it, to test the write itself"
    )]
    public async Task Setting_the_invoke_token_adds_no_row()
    {
        var id = Guid.NewGuid();
        Guid concurrency;
        using (var scope = _host.NotNull().Scope())
        {
            var store = _host.NotNull().Instances(scope);
            (await store.Insert(DraftOwner.System, id, Turnstile("Locked"))).Should().BeTrue();
            concurrency = (await store.Get(DraftOwner.System, "turnstile", id)).NotNull().Token;
        }
        var before = await History(id, SnapshotOwnerKind.System);

        using (var scope = _host.NotNull().Scope())
            (
                await _host
                    .NotNull()
                    .Instances(scope)
                    .SetInvokeToken(
                        DraftOwner.System,
                        "turnstile",
                        id,
                        concurrency,
                        new InvokeTokenWrite($"run-{Guid.NewGuid():N}")
                    )
            )
                .Should()
                .BeTrue();

        (await History(id, SnapshotOwnerKind.System))
            .Should()
            .BeEquivalentTo(before, $"a write that leaves the snapshot alone adds none. See {Adr}");
    }

    [Test]
    public async Task A_refused_advance_or_a_stale_write_adds_no_row()
    {
        var id = await Seeded(TestTurnstile.InitialJson);
        var before = await History(id);

        (await Advance("turnstile", id, "Coin", new JsonObject { ["coin"] = "penny" }))
            .Should()
            .BeOfType<AdvanceOutcome.Rejected>();
        using (var scope = _host.NotNull().Scope())
            (
                await scope
                    .ServiceProvider.GetRequiredService<ISnapshotStore>()
                    .Update(User, id, UnlockedTurnstile, Guid.NewGuid())
            )
                .Should()
                .BeFalse("the token is stale");

        (await History(id))
            .Should()
            .BeEquivalentTo(before, $"a refusal and a lost write add nothing. See {Adr}");
    }

    [Test]
    public async Task Two_concurrent_writes_with_one_token_add_one_row()
    {
        var id = await Seeded(TestTurnstile.InitialJson);
        Guid token;
        using (var scope = _host.NotNull().Scope())
            token = (
                await scope
                    .ServiceProvider.GetRequiredService<ISnapshotStore>()
                    .Get(User, "turnstile", id)
            )
                .NotNull()
                .Token;

        var writes = await Task.WhenAll(
                Enumerable
                    .Range(0, 2)
                    .Select(_ =>
                        Task.Run(async () =>
                        {
                            using var scope = _host.NotNull().Scope();
                            return await scope
                                .ServiceProvider.GetRequiredService<ISnapshotStore>()
                                .Update(User, id, UnlockedTurnstile, token)
                                .ConfigureAwait(false);
                        })
                    )
            )
            .WaitAsync(TimeSpan.FromSeconds(60), TimeProvider.System);

        writes.Count(w => w).Should().Be(1, "one token admits one write");
        (await History(id))
            .Select(t => t.Revision)
            .Should()
            .Equal([1, 2], $"the losing write adds no row. See {Adr}");
    }

    #endregion

    #region The history goes with the instance

    [Test]
    public async Task A_discard_removes_the_history()
    {
        var id = await Seeded(TestTurnstile.InitialJson);
        (await Advance("turnstile", id, "Coin", Quarter))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();
        var rowId = await _host.NotNull().RowId(id);
        (await RowHistory(_host.NotNull(), rowId)).Should().HaveCount(2);

        using (var scope = _host.NotNull().Scope())
            (
                await _host
                    .NotNull()
                    .Service(scope, "turnstile")
                    .Discard(new Actor.Client(User), id, "d1")
            )
                .Should()
                .BeOfType<DiscardOutcome.Discarded>();

        (await RowHistory(_host.NotNull(), rowId))
            .Should()
            .BeEmpty($"the history is deleted with the instance. See {Adr}");
    }

    [Test]
    public async Task The_expiry_sweep_removes_the_history()
    {
        using var host = InstanceHost.Create(provider, draftTtl: TimeSpan.FromHours(1));
        var id = Guid.NewGuid();
        await host.SeedDraft(User, id, TestTurnstile.UnlockedJson);
        var rowId = await host.RowId(id);
        (await RowHistory(host, rowId)).Should().ContainSingle();
        await host.Backdate(
            id,
            SnapshotOwnerKind.User,
            TimeProvider.System.GetUtcNow().AddDays(-2)
        );

        await host.Services.GetRequiredService<DraftSweeper>().SweepOnce();

        (await host.Rows(id)).Should().BeEmpty("the draft expired");
        (await RowHistory(host, rowId))
            .Should()
            .BeEmpty($"the sweep's delete takes the history with it. See {Adr}");
    }

    [Test]
    public async Task A_delete_by_host_code_removes_the_history_and_a_recreate_starts_a_new_one()
    {
        var id = await Seeded(TestTurnstile.InitialJson);
        (await Advance("turnstile", id, "Coin", Quarter))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();
        var first = await _host.NotNull().RowId(id);

        (
            await _host
                .NotNull()
                .Execute($"DELETE FROM {_host.NotNull().DraftTable} WHERE row_id = @p0", first)
        )
            .Should()
            .Be(1);
        (await RowHistory(_host.NotNull(), first))
            .Should()
            .BeEmpty(
                $"the database deletes the history with its row, whoever deletes it. See {Adr}"
            );

        var again = await Seeded(TestTurnstile.InitialJson);
        (await History(again))
            .Select(t => t.Revision)
            .Should()
            .Equal([1], "a new incarnation starts its own history");
    }

    #endregion
}
