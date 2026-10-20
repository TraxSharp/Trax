using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Testing;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;
using SnapshotTransition = Trax.Effect.Models.SnapshotTransition.SnapshotTransition;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// Every write of an instance's snapshot adds exactly one row to its history, with the revision that write gave it,
/// what wrote it, the command, who made it and the states it moved between, in the same transaction as the write. A
/// write that leaves the snapshot alone, and a write that is refused or loses its race, adds none, and the history
/// goes with the instance however it is removed. Each per-site test makes one write and reads the history that
/// changed. Enforces <c>Trax.Effect/docs/adr/0022-a-machine-instance-history-is-written-with-its-snapshot.md</c>.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
[Property(
    "adr",
    "Trax.Effect/docs/adr/0022-a-machine-instance-history-is-written-with-its-snapshot.md"
)]
public class TransitionHistoryTests(StoreProvider provider)
{
    private const string Adr =
        "Trax.Effect/docs/adr/0022-a-machine-instance-history-is-written-with-its-snapshot.md";

    private const string User = "history-user";

    private InstanceHost _host = null!;

    [SetUp]
    public void SetUp() => _host = InstanceHost.Create(provider, launcher: new RecordingLauncher());

    [TearDown]
    public void TearDown() => _host.Dispose();

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
    ) => HistoryOf(_host, id, kind);

    private static async Task<List<SnapshotTransition>> HistoryOf(
        InstanceHost host,
        Guid id,
        SnapshotOwnerKind kind = SnapshotOwnerKind.User
    )
    {
        var rowId = await host.RowId(id, kind);
        return await RowHistory(host, rowId);
    }

    private static async Task<List<SnapshotTransition>> RowHistory(InstanceHost host, long rowId)
    {
        using var scope = host.Scope();
        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .SnapshotTransitions.AsNoTracking()
            .Where(t => t.DraftRowId == rowId)
            .OrderBy(t => t.Revision)
            .ToListAsync();
    }

    // Makes one write and returns the one row of history it added, asserting it is exactly one, at the revision the
    // instance now holds.
    private async Task<SnapshotTransition> OneRowFor(
        Guid id,
        Func<Task> write,
        SnapshotOwnerKind kind = SnapshotOwnerKind.User
    )
    {
        var before = await History(id, kind);
        await write();
        var after = await History(id, kind);
        var added = after.Skip(before.Count).ToList();
        added.Should().ContainSingle($"one snapshot write adds one row of history. See {Adr}");
        after.Take(before.Count).Should().BeEquivalentTo(before, "history is only appended to");
        added[0]
            .Revision.Should()
            .Be(await _host.Revision(id, kind), "the row carries the revision the write gave");
        return added[0];
    }

    private async Task<Guid> Saved(string machine, string json)
    {
        var id = Guid.NewGuid();
        using var scope = _host.Scope();
        (await _host.Service(scope, machine).Autosave(User, id, json))
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
        using var scope = _host.Scope();
        return await _host.Service(scope, machine).Advance(User, id, trigger, input, requestId);
    }

    private async Task<Guid> DraftInBuilding()
    {
        var id = Guid.NewGuid();
        await _host.SeedDraft(User, id, BuildMachine.Json("Draft", Source));
        (await Advance(BuildMachine.Id, id, "Build"))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Which.Snapshot.State.Should()
            .Be("Building");
        return id;
    }

    private async Task CancelEntry(string token)
    {
        using var scope = _host.Scope();
        await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .WorkQueues.Where(w => w.ExternalId == token)
            .ExecuteUpdateAsync(s => s.SetProperty(w => w.Status, WorkQueueStatus.Cancelled));
    }

    #region One test per write site

    [Test]
    public async Task Every_creation_starts_the_history_at_revision_one()
    {
        var saved = await Saved("turnstile", TestTurnstile.InitialJson);
        var system = (await _host.Start<SystemTurnstileMachine>(NewKey())).Id;
        StartOutcome started;
        using (var scope = _host.Scope())
            started = await _host
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
        var (machine, first, second) =
            write == "upsert"
                ? ("turnstile", TestTurnstile.InitialJson, TestTurnstile.UnlockedJson)
                : (
                    LedgerMachine.Id,
                    LedgerMachine.Json(LedgerState.Open),
                    LedgerMachine.Json(LedgerState.Counting, 1, 2)
                );
        var id = await Saved(machine, first);
        // The guarded save keeps the request the row recorded; that must not make the save look like its command.
        if (write != "upsert")
            (await Advance(machine, id, nameof(LedgerTrigger.Count), requestId: "r1"))
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>();
        var stateBefore = (await _host.Rows(id)).Single().State;

        var row = await OneRowFor(
            id,
            async () =>
            {
                using var scope = _host.Scope();
                (await _host.Service(scope, machine).Autosave(User, id, second))
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
        var id = await Saved("turnstile", TestTurnstile.InitialJson);

        var row = await OneRowFor(
            id,
            async () =>
                (await Advance("turnstile", id, "Coin", Quarter))
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
                (await _host.Rows(id)).Single().UpdatedAt,
                "the row's time is the write's updated_at"
            );
    }

    [Test]
    public async Task Entering_an_invoking_state_records_the_run_it_started()
    {
        var id = Guid.NewGuid();
        await _host.SeedDraft(User, id, BuildMachine.Json("Draft", Source));

        var row = await OneRowFor(
            id,
            async () =>
                (await Advance(BuildMachine.Id, id, "Build"))
                    .Should()
                    .BeOfType<AdvanceOutcome.Advanced>()
        );

        row.Trigger.Should().Be("Build");
        row.ToState.Should().Be("Building");
        row.InvokeToken.Should()
            .NotBeNull()
            .And.Be(
                (await _host.Rows(id)).Single().InvokeToken,
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
                (await Advance(BuildMachine.Id, id, "StopBuild"))
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
        await _host.SeedDraft(User, id, OrderMachine.ReviewSnapshot(1, 2));
        var r0 = await _host.Revision(id);

        var row = await OneRowFor(
            id,
            async () =>
            {
                using var scope = _host.Scope();
                (
                    await scope
                        .ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>()
                        .EffectRunner("order")!
                        .Run(User, id, "send-1")
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
        var id = (await _host.Start<SystemTurnstileMachine>(key)).Id;

        var row = await OneRowFor(
            id,
            async () =>
            {
                using var scope = _host.Scope();
                (
                    await scope
                        .ServiceProvider.GetRequiredService<IMachineInstances>()
                        .Advance<SystemTurnstileMachine>(key, "Coin", Quarter)
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
        var token = (await _host.Rows(id)).Single().InvokeToken!;
        await CancelEntry(token);

        var row = await OneRowFor(
            id,
            async () =>
            {
                using var scope = _host.Scope();
                (
                    await scope
                        .ServiceProvider.GetRequiredService<IInvokedRunOutcomes>()
                        .Deliver(token)
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
        await CancelEntry((await _host.Rows(id)).Single().InvokeToken!);

        var row = await OneRowFor(
            id,
            async () =>
            {
                using var scope = _host.Scope();
                await scope
                    .ServiceProvider.GetRequiredService<InvokeOutcomeReconciler>()
                    .SweepOnce(CancellationToken.None);
            }
        );

        row.Kind.Should().Be(SnapshotTransitionKind.Outcome);
        row.Trigger.Should().Be("Building.cancelled");
    }

    [Test]
    public async Task A_stranded_outcome_adds_one_row_in_the_same_state()
    {
        var id = await DraftInBuilding();
        var token = (await _host.Rows(id)).Single().InvokeToken!;

        var row = await OneRowFor(
            id,
            async () =>
            {
                using var scope = _host.Scope();
                var store = _host.Instances(scope);
                var stored = await store.Get(DraftOwner.User(User), BuildMachine.Id, id);
                var building = new Snapshot
                {
                    Machine = BuildMachine.Id,
                    Version = 1,
                    State = "Building",
                    Context = Source,
                };
                (await store.StrandByInvokeToken(token, building, stored!.Token)).Should().BeTrue();
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
        await _host.SeedDraft(User, id, ReminderMachine.Json("Idle"));
        using (var scope = _host.Scope())
            (
                await _host
                    .Service(scope, ReminderMachine.Id)
                    .Advance(new Actor.Client(User, "Editor"), id, "Open")
            )
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>();
        (await RawSql.SetDueAt(_host, id, (await RawSql.Now(_host)).AddMinutes(-1))).Should().Be(1);

        var row = await OneRowFor(
            id,
            async () =>
                (
                    await _host
                        .Services.GetRequiredService<InvokeOutcomeReconciler>()
                        .FireDueTimersOnce(CancellationToken.None)
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
        await _host.Seed(User, id, ConsignmentMachine.At(ConsignmentState.Open));

        var row = await OneRowFor(
            id,
            async () =>
                (await Advance(ConsignmentMachine.Id, id, "Pack"))
                    .Should()
                    .BeOfType<AdvanceOutcome.Advanced>()
        );

        var parent = (await _host.Rows(id)).Single();
        row.InvokeToken.Should()
            .Be(parent.InvokeToken, $"the parent's row names what it spawned. See {Adr}");
        var childId = Guid.Parse(JsonNode.Parse(parent.Context)!["parcelId"]!.GetValue<string>());
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
        var id = await Saved("turnstile", TestTurnstile.InitialJson);

        var row = await OneRowFor(
            id,
            async () =>
            {
                using var scope = _host.Scope();
                var store = scope.ServiceProvider.GetRequiredService<ISnapshotStore>();
                var token = (await store.Get(User, "turnstile", id))!.Token;
                var wrote = write switch
                {
                    "Update" => await store.Update(User, id, UnlockedTurnstile, token),
                    "UpdateWithRequest" => await store.UpdateWithRequest(
                        User,
                        id,
                        UnlockedTurnstile,
                        token,
                        new AppliedRequest("r1", "Coin", "Locked")
                    ),
                    _ => await store.Upsert(User, id, UnlockedTurnstile),
                };
                wrote.Should().BeTrue();
            }
        );

        row.Kind.Should()
            .Be(
                write == "UpdateWithRequest"
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
        using (var scope = _host.Scope())
        {
            var store = _host.Instances(scope);
            (await store.Insert(DraftOwner.System, id, Turnstile("Locked"))).Should().BeTrue();
            concurrency = (await store.Get(DraftOwner.System, "turnstile", id))!.Token;
        }
        var before = await History(id, SnapshotOwnerKind.System);

        using (var scope = _host.Scope())
            (
                await _host
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
        var id = await Saved("turnstile", TestTurnstile.InitialJson);
        var before = await History(id);

        (await Advance("turnstile", id, "Coin", new JsonObject { ["coin"] = "penny" }))
            .Should()
            .BeOfType<AdvanceOutcome.Rejected>();
        using (var scope = _host.Scope())
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
        var id = await Saved("turnstile", TestTurnstile.InitialJson);
        Guid token;
        using (var scope = _host.Scope())
            token = (
                await scope
                    .ServiceProvider.GetRequiredService<ISnapshotStore>()
                    .Get(User, "turnstile", id)
            )!.Token;

        var writes = await Task.WhenAll(
                Enumerable
                    .Range(0, 2)
                    .Select(_ =>
                        Task.Run(async () =>
                        {
                            using var scope = _host.Scope();
                            return await scope
                                .ServiceProvider.GetRequiredService<ISnapshotStore>()
                                .Update(User, id, UnlockedTurnstile, token);
                        })
                    )
            )
            .WaitAsync(TimeSpan.FromSeconds(60));

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
        var id = await Saved("turnstile", TestTurnstile.InitialJson);
        (await Advance("turnstile", id, "Coin", Quarter))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();
        var rowId = await _host.RowId(id);
        (await RowHistory(_host, rowId)).Should().HaveCount(2);

        using (var scope = _host.Scope())
            (await _host.Service(scope, "turnstile").Discard(new Actor.Client(User), id, "d1"))
                .Should()
                .BeOfType<DiscardOutcome.Discarded>();

        (await RowHistory(_host, rowId))
            .Should()
            .BeEmpty($"the history is deleted with the instance. See {Adr}");
    }

    [Test]
    public async Task The_expiry_sweep_removes_the_history()
    {
        using var host = InstanceHost.Create(provider, draftTtl: TimeSpan.FromHours(1));
        var id = Guid.NewGuid();
        using (var scope = host.Scope())
            (await host.Service(scope, "turnstile").Autosave(User, id, TestTurnstile.UnlockedJson))
                .Should()
                .BeOfType<AutosaveResult.Saved>();
        var rowId = await host.RowId(id);
        (await RowHistory(host, rowId)).Should().ContainSingle();
        await host.Backdate(id, SnapshotOwnerKind.User, DateTimeOffset.UtcNow.AddDays(-2));

        await host.Services.GetRequiredService<DraftSweeper>().SweepOnce();

        (await host.Rows(id)).Should().BeEmpty("the draft expired");
        (await RowHistory(host, rowId))
            .Should()
            .BeEmpty($"the sweep's delete takes the history with it. See {Adr}");
    }

    [Test]
    public async Task A_delete_by_host_code_removes_the_history_and_a_recreate_starts_a_new_one()
    {
        var id = await Saved("turnstile", TestTurnstile.InitialJson);
        (await Advance("turnstile", id, "Coin", Quarter))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();
        var first = await _host.RowId(id);

        (await _host.Execute($"DELETE FROM {_host.DraftTable} WHERE row_id = @p0", first))
            .Should()
            .Be(1);
        (await RowHistory(_host, first))
            .Should()
            .BeEmpty(
                $"the database deletes the history with its row, whoever deletes it. See {Adr}"
            );

        var again = await Saved("turnstile", TestTurnstile.InitialJson);
        (await History(again))
            .Select(t => t.Revision)
            .Should()
            .Equal([1], "a new incarnation starts its own history");
    }

    #endregion
}

/// <summary>
/// The InMemory provider has no transaction and no foreign key, so the store writes the history and removes it with
/// the instance itself. Enforces
/// <c>Trax.Effect/docs/adr/0022-a-machine-instance-history-is-written-with-its-snapshot.md</c>.
/// </summary>
[Property(
    "adr",
    "Trax.Effect/docs/adr/0022-a-machine-instance-history-is-written-with-its-snapshot.md"
)]
public class TransitionHistoryInMemoryTests
{
    private const string Adr =
        "Trax.Effect/docs/adr/0022-a-machine-instance-history-is-written-with-its-snapshot.md";

    private const string User = "history-user";

    [Test]
    public async Task The_InMemory_provider_records_and_removes_the_history_too()
    {
        var quarter = new JsonObject { ["coin"] = "quarter" };
        var services = new ServiceCollection();
        services.AddScoped<IOrderCharge, CountingEffect>();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddStateMachines(typeof(OrderMachine).Assembly)
        );
        await using var host = services.BuildServiceProvider();
        var id = Guid.NewGuid();

        async Task<List<SnapshotTransition>> Rows()
        {
            await using var scope = host.CreateAsyncScope();
            return await scope
                .ServiceProvider.GetRequiredService<IDataContext>()
                .SnapshotTransitions.AsNoTracking()
                .OrderBy(t => t.Revision)
                .ToListAsync();
        }

        await using (var scope = host.CreateAsyncScope())
        {
            var turnstile = scope
                .ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>()
                .Service("turnstile")!;
            (await turnstile.Autosave(User, id, TestTurnstile.InitialJson))
                .Should()
                .BeOfType<AutosaveResult.Saved>();
            (await turnstile.Advance(User, id, "Coin", quarter))
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>();
        }

        var rows = await Rows();
        rows.Select(t => (t.Revision, t.Kind, t.Trigger))
            .Should()
            .Equal(
                [
                    (1L, SnapshotTransitionKind.Start, null),
                    (2L, SnapshotTransitionKind.Command, "Coin"),
                ],
                $"InMemory writes the same history. See {Adr}"
            );

        await using (var scope = host.CreateAsyncScope())
            (
                await scope
                    .ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>()
                    .Service("turnstile")!
                    .Discard(new Actor.Client(User), id, "d1")
            )
                .Should()
                .BeOfType<DiscardOutcome.Discarded>();

        (await Rows())
            .Should()
            .BeEmpty("InMemory enforces no foreign key, so the store removes it");
    }
}
