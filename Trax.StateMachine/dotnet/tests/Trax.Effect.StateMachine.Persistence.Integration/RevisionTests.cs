using System.Text.Json.Nodes;
using AwesomeAssertions;
using CsCheck;
using DbUp;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Testing;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;
using Trax.Effect.StateMachine.Persistence.Mutations;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// Every write to a machine instance bumps its revision by exactly one, because the database does the bump: a
/// <c>BEFORE UPDATE</c> trigger on Postgres and SQLite, so a path Trax does not know about (host code, a custom
/// store, an older node) bumps it too. A new row starts at 1 under a fresh <c>row_id</c>, a refusal writes nothing,
/// and the revision travels beside the snapshot, never inside its canonical wire. Each per-site test reads the
/// revision, makes one write, and reads it again. See
/// <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
public class RevisionTests(StoreProvider provider)
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string User = "u1";

    private InstanceHost _host = null!;

    [SetUp]
    public void SetUp() => _host = InstanceHost.Create(provider, launcher: new RecordingLauncher());

    [TearDown]
    public void TearDown() => _host.Dispose();

    private static JsonObject Source => new() { ["source"] = "repo" };

    private static JsonObject Quarter => new() { ["coin"] = "quarter" };

    private static MachineKey NewKey() => MachineKey.Of("revision", Guid.NewGuid().ToString());

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

    // A user's draft of the machine, saved through the draft service; or, for a machine the soft save does not
    // serve (one with an effect, an invoked train or a role-gated event), created through the store as host code
    // would. Either way it is one insert.
    private async Task<Guid> Saved(string machine, string json)
    {
        var id = Guid.NewGuid();
        using var scope = _host.Scope();
        var saved = await _host.Service(scope, machine).Autosave(User, id, json);
        if (saved is AutosaveResult.Rejected { Code: RefusalCode.SaveNotAllowed })
            (
                await scope
                    .ServiceProvider.GetRequiredService<ISnapshotStore>()
                    .Insert(User, id, Parse(json))
            )
                .Should()
                .BeTrue();
        else
            saved.Should().BeOfType<AutosaveResult.Saved>();
        return id;
    }

    private static Snapshot Parse(string json)
    {
        var node = JsonNode.Parse(json)!.AsObject();
        return new Snapshot
        {
            Machine = node["machine"]!.GetValue<string>(),
            Version = node["version"]!.GetValue<int>(),
            State = node["state"]!.GetValue<string>(),
            Context = node["context"]!.AsObject().DeepClone().AsObject(),
        };
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
        var id = await Saved(BuildMachine.Id, BuildMachine.Json("Draft", Source));
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
    public async Task A_new_draft_starts_at_revision_one_with_a_fresh_row_id()
    {
        var fast = await Saved("turnstile", TestTurnstile.InitialJson);
        var guarded = await Saved(LedgerMachine.Id, LedgerMachine.Json(LedgerState.Counting, 1));
        var system = (await _host.Start<SystemTurnstileMachine>(NewKey())).Id;

        (await _host.Revision(fast)).Should().Be(1, $"an autosave's insert starts at 1. See {Adr}");
        (await _host.Revision(guarded)).Should().Be(1, "the guarded autosave's insert too");
        (await _host.Revision(system, SnapshotOwnerKind.System))
            .Should()
            .Be(1, "a system start's insert too");

        StartOutcome started;
        using (var scope = _host.Scope())
            started = await _host
                .Service(scope, StartableMachine.Id)
                .Start(
                    new Actor.Client(User),
                    new System.Text.Json.Nodes.JsonObject { ["title"] = "t", ["copies"] = 1 },
                    requestId: $"start-{Guid.NewGuid():N}"
                );
        var created = started.Should().BeOfType<StartOutcome.Started>().Subject;
        created.Revision.Should().Be(1, "a start's insert too");
        (await _host.Revision(created.Id)).Should().Be(1);

        new[]
        {
            await _host.RowId(fast),
            await _host.RowId(guarded),
            await _host.RowId(system, SnapshotOwnerKind.System),
            await _host.RowId(created.Id),
        }
            .Should()
            .OnlyHaveUniqueItems("each new row is its own incarnation");
    }

    [TestCase("upsert")]
    [TestCase("update-with-request")]
    public async Task An_autosave_update_bumps_the_revision_by_one(string write)
    {
        // The turnstile has nothing to protect, so its autosave is the blind upsert; the ledger's is guarded (it has
        // a committed state), and overwrites through UpdateWithRequest.
        var (machine, first, second) =
            write == "upsert"
                ? ("turnstile", TestTurnstile.InitialJson, TestTurnstile.UnlockedJson)
                : (
                    LedgerMachine.Id,
                    LedgerMachine.Json(LedgerState.Counting, 1),
                    LedgerMachine.Json(LedgerState.Counting, 1, 2)
                );
        var id = await Saved(machine, first);
        var r0 = await _host.Revision(id);

        using (var scope = _host.Scope())
            (await _host.Service(scope, machine).Autosave(User, id, second))
                .Should()
                .BeOfType<AutosaveResult.Saved>();

        (await _host.Revision(id)).Should().Be(r0 + 1, $"one autosave is one write. See {Adr}");
    }

    [Test]
    public async Task An_advance_bumps_the_revision_by_one()
    {
        var id = await Saved("turnstile", TestTurnstile.InitialJson);
        var r0 = await _host.Revision(id);

        (await Advance("turnstile", id, "Coin", Quarter))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();

        (await _host.Revision(id)).Should().Be(r0 + 1, $"one advance is one write. See {Adr}");
    }

    [Test]
    public async Task Entering_an_invoking_state_bumps_once()
    {
        var id = await Saved(BuildMachine.Id, BuildMachine.Json("Draft", Source));
        var r0 = await _host.Revision(id);

        (await Advance(BuildMachine.Id, id, "Build")).Should().BeOfType<AdvanceOutcome.Advanced>();

        (await _host.Revision(id))
            .Should()
            .Be(r0 + 1, $"the state and its run's token are one write. See {Adr}");
    }

    [Test]
    public async Task Leaving_an_invoking_state_bumps_once()
    {
        var id = await DraftInBuilding();
        var r0 = await _host.Revision(id);

        (await Advance(BuildMachine.Id, id, "StopBuild"))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Which.Snapshot.State.Should()
            .Be("Draft");

        (await _host.Revision(id))
            .Should()
            .Be(r0 + 1, $"leaving and cancelling the run are one write. See {Adr}");
    }

    [Test]
    public async Task A_send_bumps_once_to_mark_and_once_to_record()
    {
        var id = await Saved("order", OrderMachine.ReviewSnapshot(1, 2));
        var r0 = await _host.Revision(id);

        using (var scope = _host.Scope())
            (
                await scope
                    .ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>()
                    .EffectRunner("order")!
                    .Run(User, id, "send-1")
            )
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>()
                .Which.Snapshot.State.Should()
                .Be("Placed");

        (await _host.Revision(id))
            .Should()
            .Be(
                r0 + 2,
                $"the send marks the row, then records the receipt, the move and the end of the mark in one write. See {Adr}"
            );
    }

    [Test]
    public async Task A_system_advance_bumps_once()
    {
        var key = NewKey();
        var id = (await _host.Start<SystemTurnstileMachine>(key)).Id;
        var r0 = await _host.Revision(id, SnapshotOwnerKind.System);

        using (var scope = _host.Scope())
            (
                await scope
                    .ServiceProvider.GetRequiredService<IMachineInstances>()
                    .Advance<SystemTurnstileMachine>(key, "Coin", Quarter)
            )
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>();

        (await _host.Revision(id, SnapshotOwnerKind.System))
            .Should()
            .Be(r0 + 1, $"a host's trusted advance is one write. See {Adr}");
    }

    [Test]
    public async Task An_outcome_applied_by_the_hook_bumps_once()
    {
        var id = await DraftInBuilding();
        var token = (await _host.Rows(id)).Single().InvokeToken!;
        await CancelEntry(token);
        var r0 = await _host.Revision(id);

        using (var scope = _host.Scope())
            (await scope.ServiceProvider.GetRequiredService<IInvokedRunOutcomes>().Deliver(token))
                .Should()
                .Be("BuildCancelled");

        (await _host.Revision(id))
            .Should()
            .Be(r0 + 1, $"an outcome is applied in one conditional update. See {Adr}");
    }

    [Test]
    public async Task An_outcome_applied_by_the_reconciler_bumps_once()
    {
        var id = await DraftInBuilding();
        var token = (await _host.Rows(id)).Single().InvokeToken!;
        await CancelEntry(token);
        var r0 = await _host.Revision(id);

        using (var scope = _host.Scope())
            await scope
                .ServiceProvider.GetRequiredService<InvokeOutcomeReconciler>()
                .SweepOnce(CancellationToken.None);

        (await _host.Rows(id)).Single().State.Should().Be("BuildCancelled");
        (await _host.Revision(id))
            .Should()
            .Be(r0 + 1, $"the sweep applies an outcome in one conditional update. See {Adr}");
    }

    [Test]
    public async Task A_stranded_outcome_bumps_once()
    {
        var id = await DraftInBuilding();
        var token = (await _host.Rows(id)).Single().InvokeToken!;
        var r0 = await _host.Revision(id);

        using (var scope = _host.Scope())
        {
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

        (await _host.Revision(id))
            .Should()
            .Be(r0 + 1, $"stranding an instance is one write. See {Adr}");
    }

    [Test]
    [LeavesStuckRuns(
        "writes an invoke token straight into the store, with no queued run behind it, to test the write itself"
    )]
    public async Task Setting_the_invoke_token_bumps_once()
    {
        var id = Guid.NewGuid();
        Guid concurrency;
        using (var scope = _host.Scope())
        {
            var store = _host.Instances(scope);
            (await store.Insert(DraftOwner.System, id, Turnstile("Locked"))).Should().BeTrue();
            concurrency = (await store.Get(DraftOwner.System, "turnstile", id))!.Token;
        }
        var r0 = await _host.Revision(id, SnapshotOwnerKind.System);

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

        (await _host.Revision(id, SnapshotOwnerKind.System))
            .Should()
            .Be(r0 + 1, $"every UPDATE bumps, the token's included. See {Adr}");
    }

    [Test]
    public async Task A_raw_UPDATE_from_host_code_bumps_the_revision()
    {
        var id = await Saved("turnstile", TestTurnstile.InitialJson);
        var r0 = await _host.Revision(id);

        (
            await _host.Execute(
                $"UPDATE {_host.DraftTable} SET context = context WHERE row_id = @p0",
                await _host.RowId(id)
            )
        )
            .Should()
            .Be(1);

        (await _host.Revision(id))
            .Should()
            .Be(
                r0 + 1,
                $"the database bumps it, so a write Trax never sees bumps it too. See {Adr}"
            );
    }

    [TestCase("Update")]
    [TestCase("UpdateWithRequest")]
    [TestCase("Upsert")]
    public async Task A_public_ISnapshotStore_write_bumps_once(string write)
    {
        var id = await Saved("turnstile", TestTurnstile.InitialJson);
        var r0 = await _host.Revision(id);

        using (var scope = _host.Scope())
        {
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
                    null
                ),
                _ => await store.Upsert(User, id, UnlockedTurnstile),
            };
            wrote.Should().BeTrue();
        }

        (await _host.Revision(id))
            .Should()
            .Be(r0 + 1, $"a host calling the public store directly bumps it once. See {Adr}");
    }

    #endregion

    #region Other revision tests

    [Test]
    public async Task A_compare_and_swap_write_reports_stored_plus_one()
    {
        var id = await Saved("turnstile", TestTurnstile.InitialJson);
        StoredSnapshot stored;
        using (var scope = _host.Scope())
            stored = (
                await scope
                    .ServiceProvider.GetRequiredService<ISnapshotStore>()
                    .Get(User, "turnstile", id)
            )!;
        stored.Revision.Should().NotBeNull($"a store read carries the row's revision. See {Adr}");

        var advanced = (await Advance("turnstile", id, "Coin", Quarter))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Subject;

        advanced
            .Revision.Should()
            .Be(
                stored.Revision + 1,
                "a write guarded by the token it read reports the revision it read plus one, without reading back"
            );
        (await _host.Revision(id)).Should().Be(advanced.Revision);
    }

    [TestCase(nameof(RefusalCode.GuardFailed))]
    [TestCase(nameof(RefusalCode.EffectBound))]
    [TestCase(nameof(RefusalCode.RequestIdReused))]
    [TestCase("ring-replay")]
    [TestCase("stale-token-conflict")]
    [TestCase("unchanged-autosave")]
    [TestCase("refused-save")]
    [TestCase(nameof(RefusalCode.InvalidInput))]
    [TestCase(nameof(RefusalCode.Forbidden))]
    public async Task A_refusal_conflict_or_replay_never_bumps(string refusal)
    {
        var (id, act) = await Refusal(refusal);
        var before = (await _host.Rows(id)).Single();
        var r0 = await _host.Revision(id);

        await act();

        (await _host.Revision(id))
            .Should()
            .Be(r0, $"a {refusal} writes nothing, so it bumps nothing. See {Adr}");
        (await _host.Rows(id))
            .Single()
            .ConcurrencyToken.Should()
            .Be(before.ConcurrencyToken, "nothing was written");
    }

    // The draft a refusal is made on, and the refusal itself, which asserts it was refused.
    private async Task<(Guid Id, Func<Task> Act)> Refusal(string refusal)
    {
        switch (refusal)
        {
            case nameof(RefusalCode.GuardFailed):
            {
                var id = await Saved("turnstile", TestTurnstile.InitialJson);
                return (
                    id,
                    async () =>
                        (
                            await Advance(
                                "turnstile",
                                id,
                                "Coin",
                                new JsonObject { ["coin"] = "penny" }
                            )
                        )
                            .Should()
                            .BeOfType<AdvanceOutcome.Rejected>()
                            .Which.Reason.Should()
                            .Be(RefusalCode.GuardFailed)
                );
            }
            case nameof(RefusalCode.InvalidInput):
            {
                var machine = "declarative-turnstile";
                var id = await Saved(
                    machine,
                    new JsonObject
                    {
                        ["machine"] = machine,
                        ["version"] = 1,
                        ["state"] = "Locked",
                        ["context"] = new JsonObject(),
                    }.ToJsonString()
                );
                return (
                    id,
                    async () =>
                        (
                            await Advance(
                                machine,
                                id,
                                "Coin",
                                new JsonObject { ["coin"] = "quarter", ["x"] = 1 }
                            )
                        )
                            .Should()
                            .BeOfType<AdvanceOutcome.Rejected>()
                            .Which.Reason.Should()
                            .Be(RefusalCode.InvalidInput)
                );
            }
            case nameof(RefusalCode.Forbidden):
            {
                var id = await Saved(
                    GatedTurnstileMachine.Id,
                    GatedTurnstileMachine.Json("Locked")
                );
                return (
                    id,
                    async () =>
                    {
                        using var scope = _host.Scope();
                        (
                            await _host
                                .Service(scope, GatedTurnstileMachine.Id)
                                .Advance(new Actor.Client(User), id, "Coin")
                        )
                            .Should()
                            .BeOfType<AdvanceOutcome.Rejected>()
                            .Which.Reason.Should()
                            .Be(RefusalCode.Forbidden);
                    }
                );
            }
            case nameof(RefusalCode.EffectBound):
            {
                var id = await Saved("order", OrderMachine.ReviewSnapshot(1));
                return (
                    id,
                    async () =>
                        (
                            await Advance(
                                "order",
                                id,
                                "Place",
                                new JsonObject { ["receipt"] = "forged" }
                            )
                        )
                            .Should()
                            .BeOfType<AdvanceOutcome.Rejected>()
                            .Which.Reason.Should()
                            .Be(RefusalCode.EffectBound)
                );
            }
            case nameof(RefusalCode.RequestIdReused):
            {
                var id = await Saved("turnstile", TestTurnstile.InitialJson);
                (await Advance("turnstile", id, "Coin", Quarter, "r1"))
                    .Should()
                    .BeOfType<AdvanceOutcome.Advanced>();
                return (
                    id,
                    async () =>
                        (await Advance("turnstile", id, "Push", requestId: "r1"))
                            .Should()
                            .BeOfType<AdvanceOutcome.Rejected>()
                            .Which.Reason.Should()
                            .Be(RefusalCode.RequestIdReused)
                );
            }
            case "ring-replay":
            {
                var id = await Saved("turnstile", TestTurnstile.InitialJson);
                (await Advance("turnstile", id, "Coin", Quarter, "r1"))
                    .Should()
                    .BeOfType<AdvanceOutcome.Advanced>();
                return (
                    id,
                    async () =>
                        (await Advance("turnstile", id, "Coin", Quarter, "r1"))
                            .Should()
                            .BeOfType<AdvanceOutcome.Advanced>("a retry replays")
                            .Which.Snapshot.State.Should()
                            .Be("Unlocked")
                );
            }
            case "stale-token-conflict":
            {
                var id = await Saved("turnstile", TestTurnstile.InitialJson);
                return (
                    id,
                    async () =>
                    {
                        using var scope = _host.Scope();
                        (
                            await scope
                                .ServiceProvider.GetRequiredService<ISnapshotStore>()
                                .Update(User, id, UnlockedTurnstile, Guid.NewGuid())
                        )
                            .Should()
                            .BeFalse("the token is stale");
                    }
                );
            }
            case "unchanged-autosave":
            {
                // A draft in a committed state answers an identical save without writing.
                var closed = LedgerMachine.Json(LedgerState.Closed, 1);
                var id = await Saved(LedgerMachine.Id, LedgerMachine.Json(LedgerState.Counting, 1));
                (await Advance(LedgerMachine.Id, id, nameof(LedgerTrigger.Close)))
                    .Should()
                    .BeOfType<AdvanceOutcome.Advanced>();
                return (
                    id,
                    async () =>
                    {
                        using var scope = _host.Scope();
                        (await _host.Service(scope, LedgerMachine.Id).Autosave(User, id, closed))
                            .Should()
                            .BeOfType<AutosaveResult.Saved>();
                    }
                );
            }
            case "refused-save":
            {
                var id = await Saved("turnstile", TestTurnstile.InitialJson);
                return (
                    id,
                    async () =>
                    {
                        using var scope = _host.Scope();
                        (
                            await _host
                                .Service(scope, "turnstile")
                                .Autosave(
                                    User,
                                    id,
                                    TestTurnstile.Machine.Serialize(
                                        Turnstile("Locked", new JsonObject { ["x"] = 1 })
                                    )
                                )
                        )
                            .Should()
                            .BeOfType<AutosaveResult.Rejected>();
                    }
                );
            }
            default:
                throw new ArgumentOutOfRangeException(nameof(refusal), refusal, null);
        }
    }

    [Test]
    public async Task Delete_then_recreate_under_one_id_is_a_new_row_id_and_restarts_at_one()
    {
        var id = await Saved("turnstile", TestTurnstile.InitialJson);
        (await Advance("turnstile", id, "Coin", Quarter))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>();
        var first = await _host.RowId(id);
        (await _host.Revision(id)).Should().Be(2);

        using (var scope = _host.Scope())
            await scope
                .ServiceProvider.GetRequiredService<ISnapshotStore>()
                .Delete(User, "turnstile", id);
        using (var scope = _host.Scope())
            (await _host.Service(scope, "turnstile").Autosave(User, id, TestTurnstile.InitialJson))
                .Should()
                .BeOfType<AutosaveResult.Saved>();

        (await _host.RowId(id))
            .Should()
            .NotBe(first, $"a re-created instance is a new incarnation. See {Adr}");
        (await _host.Revision(id)).Should().Be(1, "its ordering starts again");
    }

    [Test]
    public async Task Two_concurrent_writes_with_one_token_bump_once()
    {
        var id = await Saved("turnstile", TestTurnstile.InitialJson);
        Guid token;
        using (var scope = _host.Scope())
            token = (
                await scope
                    .ServiceProvider.GetRequiredService<ISnapshotStore>()
                    .Get(User, "turnstile", id)
            )!.Token;
        var r0 = await _host.Revision(id);

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
        (await _host.Revision(id))
            .Should()
            .Be(r0 + 1, $"the losing write bumps nothing, never r0 + 2. See {Adr}");
    }

    [Test]
    public async Task A_revision_beyond_two_to_the_53_round_trips_exactly()
    {
        const long Big = 9_007_199_254_740_993;
        const string Owner = "u-big";
        var id = await Saved("turnstile", TestTurnstile.InitialJson);
        var original = await _host.RowId(id);

        // Inserted with the revision set, which no UPDATE trigger touches, then the original row removed.
        await _host.Execute(
            $"INSERT INTO {_host.DraftTable} "
                + "(id, user_key, owner_kind, machine, version, state, context, concurrency_token, updated_at, revision) "
                + "SELECT id, @p1, owner_kind, machine, version, state, context, concurrency_token, updated_at, @p2 "
                + $"FROM {_host.DraftTable} WHERE row_id = @p0",
            original,
            Owner,
            Big
        );
        await _host.Execute($"DELETE FROM {_host.DraftTable} WHERE row_id = @p0", original);

        AdvanceSnapshotOutput output;
        using (var scope = _host.Scope())
            output = await new AdvanceSnapshotJunction(
                scope.ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>(),
                new FakePrincipal(Owner)
            ).Run(
                new AdvanceSnapshotInput
                {
                    Machine = "turnstile",
                    Id = id,
                    Trigger = "Coin",
                    Input = Quarter.ToJsonString(),
                }
            );

        output.Problem.Should().BeNull();
        output
            .Revision.Should()
            .Be(
                Big + 1,
                $"the output carries the revision beside the snapshot, exactly. See {Adr}"
            );
        using (var scope = _host.Scope())
            (
                await scope
                    .ServiceProvider.GetRequiredService<ISnapshotStore>()
                    .Get(Owner, "turnstile", id)
            )!
                .Revision.Should()
                .Be(Big + 1, "a store read carries it exactly too");
        (await _host.Revision(id)).Should().Be(Big + 1);
    }

    [Test]
    public async Task The_canonical_wire_never_carries_revision_or_dueAt()
    {
        var id = await Saved("turnstile", TestTurnstile.InitialJson);

        async Task<string> Wire()
        {
            using var scope = _host.Scope();
            var service = _host.Service(scope, "turnstile");
            return service.Serialize(
                (await service.Load(User, id))
                    .Should()
                    .BeOfType<LoadResult.Loaded>()
                    .Subject.Snapshot
            );
        }

        var before = await Wire();
        // A write that bumps the revision, and changes nothing else the snapshot holds.
        await _host.Execute(
            $"UPDATE {_host.DraftTable} SET context = context WHERE row_id = @p0",
            await _host.RowId(id)
        );
        var after = await Wire();

        after
            .Should()
            .Be(before, $"the revision travels beside the snapshot, never in it. See {Adr}");
        foreach (var wire in new[] { before, after })
            wire.Should().NotContainAny("revision", "dueAt", "due_at", "rowId", "row_id");

        LoadSnapshotOutput loaded;
        using (var scope = _host.Scope())
            loaded = await new LoadSnapshotJunction(
                scope.ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>(),
                new FakePrincipal(User)
            ).Run(new LoadSnapshotInput { Machine = "turnstile", Id = id });
        loaded.Snapshot.Should().Be(before, "the load mutation answers with the canonical wire");

        // The twin's result is compared byte for byte with the server's canonical wire, so a revision inside it
        // would make every clientResult diverge.
        var clientResult = TestTurnstile.Machine.Serialize(UnlockedTurnstile);
        using (var scope = _host.Scope())
            (
                await _host
                    .Service(scope, "turnstile")
                    .Advance(User, id, "Coin", Quarter, requestId: null, clientResult)
            )
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>(
                    "the canonical wire of the result is what the twin computes"
                );
    }

    [Test]
    public async Task Existing_rows_get_revision_one_and_a_row_id_after_the_migration()
    {
        if (provider == StoreProvider.Postgres)
            await PostgresRowsBeforeTheRevisionMigration();
        else
            await SqliteRowsBeforeTheRevisionMigration();
    }

    // The last migration of each provider before the revision column, which runs on the rows written below.
    private const int LastPostgresMigrationBeforeRevision = 75;
    private const int LastSqliteMigrationBeforeRevision = 37;

    private static int MigrationNumber(string resourceName)
    {
        var file = resourceName[
            (
                resourceName.IndexOf(".Migrations.", StringComparison.Ordinal)
                + ".Migrations.".Length
            )..
        ];
        return int.Parse(
            file[..file.IndexOf('_')],
            System.Globalization.CultureInfo.InvariantCulture
        );
    }

    private static async Task PostgresRowsBeforeTheRevisionMigration()
    {
        var database = $"trax_revision_migration_{Environment.ProcessId}";
        var maintenance =
            $"Host=localhost;Port={TestPostgres.Port};Username=trax;Password=trax123;Database=postgres";
        var connectionString =
            $"Host=localhost;Port={TestPostgres.Port};Username=trax;Password=trax123;Database={database};Include Error Detail=true";

        await using (var admin = new NpgsqlConnection(maintenance))
        {
            await admin.OpenAsync();
            await Exec(admin, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
            await Exec(admin, $"CREATE DATABASE {database}");
        }

        try
        {
            await using (var setup = new NpgsqlConnection(connectionString))
            {
                await setup.OpenAsync();
                await Exec(setup, "CREATE SCHEMA IF NOT EXISTS trax;");
            }
            var upTo = DeployChanges
                .To.PostgresqlDatabase(connectionString)
                .JournalToPostgresqlTable("trax", "migrations")
                .WithScriptsEmbeddedInAssembly(
                    typeof(Trax.Effect.Data.Postgres.Utils.DatabaseMigrator).Assembly,
                    name => MigrationNumber(name) <= LastPostgresMigrationBeforeRevision
                )
                .LogToNowhere()
                .Build()
                .PerformUpgrade();
            upTo.Successful.Should().BeTrue(upTo.Error?.ToString());

            await using (var before = new NpgsqlConnection(connectionString))
            {
                await before.OpenAsync();
                await Exec(
                    before,
                    """
                    INSERT INTO trax.snapshot_draft
                        (id, user_key, owner_kind, machine, version, state, context, concurrency_token, updated_at)
                    VALUES
                        (gen_random_uuid(), 'u1', 'user', 'turnstile', 1, 'Locked', '{}', gen_random_uuid(), now()),
                        (gen_random_uuid(), NULL, 'system', 'turnstile', 1, 'Locked', '{}', gen_random_uuid(), now())
                    """
                );
            }

            await Trax.Effect.Data.Postgres.Utils.DatabaseMigrator.Migrate(connectionString);

            await using var after = new NpgsqlConnection(connectionString);
            await after.OpenAsync();
            await using var read = after.CreateCommand();
            read.CommandText = "SELECT revision, row_id FROM trax.snapshot_draft ORDER BY row_id";
            await AssertEachRowAtOne(read);
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var admin = new NpgsqlConnection(maintenance);
            await admin.OpenAsync();
            await Exec(admin, $"DROP DATABASE IF EXISTS {database} WITH (FORCE)");
        }
    }

    private static async Task SqliteRowsBeforeTheRevisionMigration()
    {
        var file = Path.Combine(Path.GetTempPath(), $"sm_revision_{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={file}";
        try
        {
            var upTo = DeployChanges
                .To.SqliteDatabase(connectionString)
                .WithScriptsEmbeddedInAssembly(
                    typeof(Trax.Effect.Data.Sqlite.Utils.DatabaseMigrator).Assembly,
                    name => MigrationNumber(name) <= LastSqliteMigrationBeforeRevision
                )
                .WithTransactionPerScript()
                .LogToNowhere()
                .Build()
                .PerformUpgrade();
            upTo.Successful.Should().BeTrue(upTo.Error?.ToString());

            await using (var before = new SqliteConnection(connectionString))
            {
                await before.OpenAsync();
                await using var insert = before.CreateCommand();
                insert.CommandText = $"""
                    INSERT INTO snapshot_draft
                        (id, user_key, owner_kind, machine, version, state, context, concurrency_token, updated_at)
                    VALUES
                        ('{Guid.NewGuid():D}', 'u1', 0, 'turnstile', 1, 'Locked', '{"{}"}', '{Guid.NewGuid():D}',
                         '2026-10-09 00:00:00+00:00'),
                        ('{Guid.NewGuid():D}', NULL, 1, 'turnstile', 1, 'Locked', '{"{}"}', '{Guid.NewGuid():D}',
                         '2026-10-09 00:00:00+00:00')
                    """;
                await insert.ExecuteNonQueryAsync();
            }

            await Trax.Effect.Data.Sqlite.Utils.DatabaseMigrator.Migrate(connectionString);

            await using var after = new SqliteConnection(connectionString);
            await after.OpenAsync();
            await using var read = after.CreateCommand();
            read.CommandText = "SELECT revision, row_id FROM snapshot_draft ORDER BY row_id";
            await AssertEachRowAtOne(read);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            File.Delete(file);
        }
    }

    private static async Task AssertEachRowAtOne(System.Data.Common.DbCommand read)
    {
        var rows = new List<(long Revision, long RowId)>();
        await using (var reader = await read.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                rows.Add((reader.GetInt64(0), reader.GetInt64(1)));

        rows.Should().HaveCount(2);
        rows.Should()
            .OnlyContain(
                r => r.Revision == 1,
                $"a row written before the column starts at 1. See {Adr}"
            );
        rows.Select(r => r.RowId).Should().OnlyHaveUniqueItems();
    }

    private static async Task Exec(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    [Test]
    public async Task The_EF_InMemory_provider_bumps_or_is_refused_as_the_ADR_says()
    {
        var services = new ServiceCollection();
        services.AddScoped<IOrderCharge, CountingEffect>();
        services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UseInMemory())
                .AddStateMachines(typeof(OrderMachine).Assembly)
        );
        await using var host = services.BuildServiceProvider();
        var id = Guid.NewGuid();

        async Task<long?> Stored()
        {
            await using var scope = host.CreateAsyncScope();
            return (
                await scope
                    .ServiceProvider.GetRequiredService<ISnapshotStore>()
                    .Get(User, "turnstile", id)
            )!.Revision;
        }

        await using (var scope = host.CreateAsyncScope())
            (
                await scope
                    .ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>()
                    .Service("turnstile")!
                    .Autosave(User, id, TestTurnstile.InitialJson)
            )
                .Should()
                .BeOfType<AutosaveResult.Saved>();
        (await Stored())
            .Should()
            .Be(1, $"InMemory has no trigger, so the store bumps it. See {Adr}");

        AdvanceOutcome advanced;
        await using (var scope = host.CreateAsyncScope())
            advanced = await scope
                .ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>()
                .Service("turnstile")!
                .Advance(User, id, "Coin", Quarter);
        advanced.Should().BeOfType<AdvanceOutcome.Advanced>().Which.Revision.Should().Be(2);
        (await Stored()).Should().Be(2);
    }

    [Test]
    public async Task A_custom_store_without_revision_still_works()
    {
        var store = new RevisionlessStore();
        var service = TestTurnstile.Service(store);
        var id = Guid.NewGuid();

        (await service.Autosave(User, id, TestTurnstile.InitialJson))
            .Should()
            .BeOfType<AutosaveResult.Saved>();
        var advanced = (await service.Advance(User, id, "Coin", Quarter))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>(
                $"a store that records no revision keeps working; the revision is optional. See {Adr}"
            )
            .Subject;

        advanced.Snapshot.State.Should().Be("Unlocked");
        advanced.Revision.Should().BeNull("this store records none");
        var stored = (await ((ISnapshotStore)store).Get(User, id))!;
        stored.Revision.Should().BeNull();
        stored.RowId.Should().BeNull();
    }

    /// <summary>
    /// A store written before the revision existed: it builds <see cref="StoredSnapshot"/> through its positional
    /// constructor and sets none of the later init properties.
    /// </summary>
    private sealed class RevisionlessStore : ISnapshotStore
    {
        private readonly Dictionary<(string, Guid), StoredSnapshot> _rows = new();

        public Task<StoredSnapshot?> Get(
            string userKey,
            Guid id,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(_rows.GetValueOrDefault((userKey, id)));

        public Task Delete(string userKey, Guid id, CancellationToken cancellationToken = default)
        {
            _rows.Remove((userKey, id));
            return Task.CompletedTask;
        }

        public Task<bool> Upsert(
            string userKey,
            Guid id,
            Snapshot snapshot,
            CancellationToken cancellationToken = default
        )
        {
            _rows[(userKey, id)] = new StoredSnapshot(
                TestTurnstile.Machine.Serialize(snapshot),
                Guid.NewGuid(),
                null,
                DateTimeOffset.UtcNow
            );
            return Task.FromResult(true);
        }

        public Task<bool> Update(
            string userKey,
            Guid id,
            Snapshot snapshot,
            Guid expectedToken,
            string? requestId = null,
            CancellationToken cancellationToken = default
        )
        {
            if (!_rows.TryGetValue((userKey, id), out var row) || row.Token != expectedToken)
                return Task.FromResult(false);
            _rows[(userKey, id)] = new StoredSnapshot(
                TestTurnstile.Machine.Serialize(snapshot),
                Guid.NewGuid(),
                requestId,
                DateTimeOffset.UtcNow
            );
            return Task.FromResult(true);
        }
    }

    #endregion

    #region Property

    // Sequences per provider, each against fresh instances, checked after every step.
    private const int Iterations = 25;

    private enum Kind
    {
        Save,
        AdvanceValid,
        AdvanceInvalid,
        Replay,
        StaleAdvance,
        Send,
        Reset,
        Outcome,
    }

    private readonly record struct Op(Kind Kind, int Variant)
    {
        public override string ToString() =>
            Kind == Kind.Save ? $"Save({Variant})" : Kind.ToString();
    }

    private static readonly Gen<Op> AnOperation = Gen.Frequency(
        (4, Gen.Int[0, 2].Select(v => new Op(Kind.Save, v))),
        (6, Gen.Const(new Op(Kind.AdvanceValid, 0))),
        (2, Gen.Const(new Op(Kind.AdvanceInvalid, 0))),
        (3, Gen.Const(new Op(Kind.Replay, 0))),
        (2, Gen.Const(new Op(Kind.StaleAdvance, 0))),
        (3, Gen.Const(new Op(Kind.Send, 0))),
        (2, Gen.Const(new Op(Kind.Reset, 0))),
        (4, Gen.Const(new Op(Kind.Outcome, 0)))
    );

    [Test]
    public Task Revision_equals_the_number_of_successful_writes() =>
        AnOperation
            .Array[1, 20]
            .SampleAsync(
                async ops => await new RevisionModel(this).Run(ops),
                iter: Iterations,
                threads: 1,
                print: ops =>
                    string.Join(Environment.NewLine, ops.Select((op, i) => $"  {i}: {op}"))
            );

    /// <summary>
    /// What an order draft and a build draft should hold after each step: the order's state, item count and last
    /// recorded request; the build's state; and each one's revision, the count of writes that changed its row.
    /// </summary>
    private sealed class RevisionModel(RevisionTests test)
    {
        // Each run is its own user: the builds a run leaves building hold queued runs, and one user may hold
        // only so many at once.
        private readonly string _user = $"model-{Guid.NewGuid():N}";

        private readonly Guid _order = Guid.NewGuid();
        private readonly Guid _build = Guid.NewGuid();
        private int _step;

        private string? _orderState;
        private int _items;

        private (string Id, string Trigger)? _last;
        private long _orderRevision;

        private string? _buildState;
        private long _buildRevision;

        private static readonly (string State, int Items)[] Saves =
        [
            ("Review", 1),
            ("Review", 2),
            ("Draft", 1),
        ];

        private InstanceHost Host => test._host;

        public async Task Run(Op[] ops)
        {
            foreach (var op in ops)
            {
                _step++;
                await Apply(op);
                await Check(op);
            }
        }

        private async Task Apply(Op op)
        {
            switch (op.Kind)
            {
                case Kind.Save:
                    var (state, items) = Saves[op.Variant];
                    await Save(state, items);
                    break;
                case Kind.Reset:
                    await Save("Draft", 0);
                    break;
                case Kind.AdvanceValid:
                    if (_orderState is { } current)
                        await Fire(TriggerFrom(current), $"a{_step}");
                    else
                        (await Advance("Next", $"a{_step}"))
                            .Should()
                            .BeOfType<AdvanceOutcome.NotFound>();
                    break;
                case Kind.AdvanceInvalid:
                    // Place is effect-bound from Review and has no edge elsewhere: refused either way.
                    (
                        await Advance(
                            "Place",
                            $"a{_step}",
                            new JsonObject { ["receipt"] = "forged" }
                        )
                    )
                        .Should()
                        .NotBeOfType<AdvanceOutcome.Advanced>();
                    break;
                case Kind.Replay:
                    await Replay();
                    break;
                case Kind.StaleAdvance:
                    using (var scope = Host.Scope())
                        (
                            await scope
                                .ServiceProvider.GetRequiredService<ISnapshotStore>()
                                .UpdateWithRequest(
                                    _user,
                                    _order,
                                    Order("Review", 3),
                                    Guid.NewGuid(),
                                    null
                                )
                        )
                            .Should()
                            .BeFalse("a stale token writes nothing");
                    break;
                case Kind.Send:
                    await Send();
                    break;
                case Kind.Outcome:
                    await StepBuild();
                    break;
            }
        }

        private static string TriggerFrom(string state) =>
            state switch
            {
                "Draft" => "Next",
                "Review" => "Back",
                _ => "Reset",
            };

        private static Snapshot Order(string state, int items)
        {
            var array = new JsonArray();
            for (var i = 1; i <= items; i++)
                array.Add(i);
            return new Snapshot
            {
                Machine = "order",
                Version = 1,
                State = state,
                Context = new JsonObject { ["items"] = array, ["receipt"] = null },
            };
        }

        private async Task<AdvanceOutcome> Advance(
            string trigger,
            string requestId,
            JsonNode? input = null
        )
        {
            using var scope = Host.Scope();
            return await Host.Service(scope, "order")
                .Advance(_user, _order, trigger, input, requestId);
        }

        // The soft save does not serve the order, which has an effect, and writes nothing; the model's save is host
        // code writing the draft through the store instead, under the rule the soft save once applied: from Placed
        // only a reset to the initial state is written.
        private async Task Save(string state, int items)
        {
            using var scope = Host.Scope();
            (
                await Host.Service(scope, "order")
                    .Autosave(_user, _order, TestOrder.Machine.Serialize(Order(state, items)))
            )
                .Should()
                .BeOfType<AutosaveResult.Rejected>()
                .Which.Code.Should()
                .Be(RefusalCode.SaveNotAllowed);

            if (_orderState == "Placed" && state != "Draft")
                return;
            var store = scope.ServiceProvider.GetRequiredService<ISnapshotStore>();
            var stored = await store.Get(_user, "order", _order);
            (
                stored is null
                    ? await store.Insert(_user, _order, Order(state, items))
                    : await store.UpdateWithRequest(
                        _user,
                        _order,
                        Order(state, items),
                        stored.Token,
                        stored.LastRequest
                    )
            )
                .Should()
                .BeTrue();
            if (_orderState is null)
            {
                _last = null;
                _orderRevision = 0;
            }
            _orderState = state;
            _items = items;
            _orderRevision++;
        }

        // Fires trigger from the current state as a new request, as the service would.
        private async Task Fire(string trigger, string requestId)
        {
            var from = _orderState!;
            var outcome = await Advance(trigger, requestId);
            if (from == "Draft" && _items == 0)
            {
                // Review holds at least one item, so Next from an empty draft is refused.
                outcome.Should().BeOfType<AdvanceOutcome.Rejected>();
                return;
            }
            outcome.Should().BeOfType<AdvanceOutcome.Advanced>();
            (_orderState, _items) = trigger switch
            {
                "Next" => ("Review", _items),
                "Back" => ("Draft", _items),
                _ => ("Draft", 0),
            };
            _last = (requestId, trigger);
            _orderRevision++;
        }

        private async Task Replay()
        {
            if (_orderState is null || _last is not { } last)
                return;
            if (last.Trigger == "Place")
            {
                // A send's request: effect-bound from Review, a replay anywhere else. Neither writes.
                (await Advance("Place", last.Id, new JsonObject { ["receipt"] = "forged" }))
                    .Should()
                    .NotBeNull();
                return;
            }
            // A retry of the recorded request replays wherever the draft is, the state it fired from included, and
            // writes nothing.
            (await Advance(last.Trigger, last.Id))
                .Should()
                .BeOfType<AdvanceOutcome.Advanced>("a retry of the recorded request replays");
        }

        private async Task Send()
        {
            AdvanceOutcome outcome;
            using (var scope = Host.Scope())
                outcome = await scope
                    .ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>()
                    .EffectRunner("order")!
                    .Run(_user, _order, $"s{_step}");

            switch (_orderState)
            {
                case null:
                    outcome.Should().BeOfType<AdvanceOutcome.NotFound>();
                    return;
                case "Review":
                    outcome
                        .Should()
                        .BeOfType<AdvanceOutcome.Advanced>(
                            $"a send from Review places the order ({outcome})"
                        )
                        .Which.Snapshot.State.Should()
                        .Be("Placed");
                    _orderState = "Placed";
                    _last = ($"s{_step}", "Place");
                    // The mark, then the record.
                    _orderRevision += 2;
                    return;
                case "Placed":
                    outcome
                        .Should()
                        .BeOfType<AdvanceOutcome.Advanced>("a send of a placed order replays");
                    return;
                default:
                    outcome.Should().BeOfType<AdvanceOutcome.Rejected>();
                    return;
            }
        }

        // Moves the build draft around its loop: created, entering Building, its run's outcome, and a retry.
        private async Task StepBuild()
        {
            switch (_buildState)
            {
                case null:
                    using (var scope = Host.Scope())
                        (
                            await scope
                                .ServiceProvider.GetRequiredService<ISnapshotStore>()
                                .Insert(_user, _build, Parse(BuildMachine.Json("Draft", Source)))
                        )
                            .Should()
                            .BeTrue();
                    _buildState = "Draft";
                    break;
                case "Building":
                    var token = (await Host.Rows(_build)).Single().InvokeToken!;
                    await test.CancelEntry(token);
                    using (var scope = Host.Scope())
                        (
                            await scope
                                .ServiceProvider.GetRequiredService<IInvokedRunOutcomes>()
                                .Deliver(token)
                        )
                            .Should()
                            .Be("BuildCancelled");
                    _buildState = "BuildCancelled";
                    break;
                default:
                    using (var scope = Host.Scope())
                        (
                            await Host.Service(scope, BuildMachine.Id)
                                .Advance(_user, _build, _buildState == "Draft" ? "Build" : "Retry")
                        )
                            .Should()
                            .BeOfType<AdvanceOutcome.Advanced>();
                    _buildState = "Building";
                    break;
            }
            _buildRevision++;
        }

        private async Task Check(Op op)
        {
            if (_orderState is { } state)
            {
                var row = (await Host.Rows(_order)).Single();
                row.State.Should().Be(state, $"after step {_step} ({op})");
                (JsonNode.Parse(row.Context)!["items"] as JsonArray)!
                    .Count.Should()
                    .Be(_items, $"after step {_step} ({op})");
                (await Host.Revision(_order))
                    .Should()
                    .Be(
                        _orderRevision,
                        $"the order was written {_orderRevision} times by step {_step} ({op}). See {Adr}"
                    );
            }

            if (_buildState is { } build)
            {
                (await Host.Rows(_build))
                    .Single()
                    .State.Should()
                    .Be(build, $"after step {_step} ({op})");
                (await Host.Revision(_build))
                    .Should()
                    .Be(
                        _buildRevision,
                        $"the build was written {_buildRevision} times by step {_step} ({op}). See {Adr}"
                    );
            }
        }
    }

    #endregion
}

/// <summary>
/// Concurrent advances of one instance, on Postgres: each successful write takes the next revision, and none is
/// repeated or skipped. See
/// <c>Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md</c>.
/// </summary>
[TestFixture]
[Property(
    "adr",
    "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md"
)]
public class RevisionConcurrencyTests
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private const string User = "u1";

    private InstanceHost _host = null!;

    [SetUp]
    public void SetUp() => _host = InstanceHost.Create(StoreProvider.Postgres);

    [TearDown]
    public void TearDown() => _host.Dispose();

    [Test]
    public async Task Twenty_concurrent_advances_leave_contiguous_revisions()
    {
        const int Callers = 20;
        var id = Guid.NewGuid();
        using (var scope = _host.Scope())
            (
                await scope
                    .ServiceProvider.GetRequiredService<ISnapshotStore>()
                    .Upsert(
                        User,
                        id,
                        new Snapshot
                        {
                            Machine = BuildMachine.Id,
                            Version = 1,
                            State = "Draft",
                            Context = new JsonObject { ["source"] = "repo" },
                        }
                    )
            ).Should().BeTrue();

        // Edit is a self-loop on Draft: every caller's advance is valid, and only the token race refuses one.
        var advanced = await Task.WhenAll(
                Enumerable
                    .Range(0, Callers)
                    .Select(_ =>
                        Task.Run(async () =>
                        {
                            for (var attempt = 0; attempt < 500; attempt++)
                            {
                                using var scope = _host.Scope();
                                var outcome = await _host
                                    .Service(scope, BuildMachine.Id)
                                    .Advance(User, id, "Edit");
                                if (outcome is AdvanceOutcome.Advanced done)
                                    return done;
                                outcome.Should().BeOfType<AdvanceOutcome.Conflict>();
                            }
                            throw new InvalidOperationException("an advance lost every race");
                        })
                    )
            )
            .WaitAsync(TimeSpan.FromSeconds(120));

        (await _host.Revision(id))
            .Should()
            .Be(1 + Callers, $"the insert and each successful advance bumped once. See {Adr}");
        advanced
            .Select(a => a.Revision)
            .Should()
            .BeEquivalentTo(
                Enumerable.Range(2, Callers).Select(r => (long?)r),
                "each successful write reports the revision it made, none repeated or skipped"
            );
    }
}
