using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;
using Trax.Effect.StateMachine.Persistence.Mutations;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// What the effect runner commits, and what keeps its claim, when the draft is written to, reset or expired while
/// the effect runs, or when the effect ends in a cancellation: the receipt is recorded only on the content the
/// effect ran on, and an effect that may have happened never runs a second time.
///
/// <para>Enforces <c>docs/adr/0017-only-the-effect-runner-reaches-a-committed-state.md</c>.</para>
/// </summary>
[Property("adr", "docs/adr/0017-only-the-effect-runner-reaches-a-committed-state.md")]
public class EffectCommitIntegrityTests
{
    private const string Adr =
        "the effect's receipt is recorded only on the draft the effect ran on, and an effect that may have "
        + "happened never runs again. See docs/adr/0017-only-the-effect-runner-reaches-a-committed-state.md";

    private const string User = "u";

    private static ISnapshotDraftService Orders(IEffectClaimStore? claims = null) =>
        new OrderMachine().CreateService(TestDb.NewStore(), claims ?? TestDb.NewClaims());

    private static ISnapshotEffectRunner Runner(IOrderCharge charge)
    {
        var context = TestDb.NewContext();
        var claims = TestDb.NewClaims(context);
        var machine = new OrderMachine();
        return machine.CreateEffectRunner(
            machine.CreateService(TestDb.NewStore(context), claims),
            new IdempotentEffect(claims),
            new ServiceCollection().AddSingleton(charge).BuildServiceProvider()
        )!;
    }

    private static string EffectKey(Guid id) => $"order:place:{User}:{id}";

    private static readonly string DraftSnapshot = new JsonObject
    {
        ["machine"] = "order",
        ["version"] = 1,
        ["state"] = "Draft",
        ["context"] = new JsonObject { ["items"] = new JsonArray(), ["receipt"] = null },
    }.ToJsonString();

    private static async Task<Guid> SeedReview(params int[] items)
    {
        var id = Guid.NewGuid();
        (await Orders().Autosave(User, id, OrderMachine.ReviewSnapshot(items)))
            .Should()
            .BeOfType<AutosaveResult.Saved>();
        return id;
    }

    private static async Task<Snapshot> Stored(Guid id) =>
        (await Orders().Load(User, id)).Should().BeOfType<LoadResult.Loaded>().Which.Snapshot;

    private static int ItemCount(Snapshot snapshot) => snapshot.Context["items"]!.AsArray().Count;

    [Test]
    public async Task An_edit_during_the_effect_is_not_committed_with_its_receipt()
    {
        var id = await SeedReview(1);
        var charge = new GatedCharge();

        var send = Task.Run(() => Runner(charge).Run(User, id, "req-1"));
        await charge.Entered;
        (await Orders().Autosave(User, id, OrderMachine.ReviewSnapshot(1, 2, 3, 4, 5)))
            .Should()
            .BeOfType<AutosaveResult.Saved>();
        charge.Release();

        (await send).Should().BeOfType<AdvanceOutcome.Conflict>(Adr);
        var stored = await Stored(id);
        stored.State.Should().Be("Review");
        ItemCount(stored).Should().Be(5);
        charge.Calls.Should().Be(1, Adr);
        charge.ChargedItemCounts.Should().Equal([1], Adr);
    }

    [Test]
    public async Task A_send_after_an_edit_during_the_effect_neither_charges_again_nor_commits_the_receipt_on_the_edit()
    {
        var id = await SeedReview(1);
        var charge = new GatedCharge();

        var send = Task.Run(() => Runner(charge).Run(User, id, "req-1"));
        await charge.Entered;
        await Orders().Autosave(User, id, OrderMachine.ReviewSnapshot(1, 2));
        charge.Release();
        await send;

        var retry = await Runner(charge).Run(User, id, "req-2");

        retry
            .Should()
            .BeOfType<AdvanceOutcome.Rejected>()
            .Which.Reason.Should()
            .Be("draft-changed", Adr);
        charge.Calls.Should().Be(1, Adr);
        var stored = await Stored(id);
        stored.State.Should().Be("Review", Adr);
        ItemCount(stored).Should().Be(2);
        stored.Context["receipt"].Should().BeNull(Adr);
    }

    [Test]
    public async Task A_send_after_the_draft_is_restored_to_what_the_effect_ran_on_replays_its_receipt()
    {
        var id = await SeedReview(1);
        var charge = new GatedCharge();

        var send = Task.Run(() => Runner(charge).Run(User, id, "req-1"));
        await charge.Entered;
        await Orders().Autosave(User, id, OrderMachine.ReviewSnapshot(1, 2));
        charge.Release();
        (await send).Should().BeOfType<AdvanceOutcome.Conflict>();
        await Orders().Autosave(User, id, OrderMachine.ReviewSnapshot(1));

        var retry = await Runner(charge).Run(User, id, "req-2");

        var placed = retry.Should().BeOfType<AdvanceOutcome.Advanced>().Which.Snapshot;
        placed.Context["receipt"]!.GetValue<string>().Should().Be("receipt-1");
        ItemCount(placed).Should().Be(1);
        charge.Calls.Should().Be(1, Adr);
    }

    [Test]
    public async Task An_unedited_draft_whose_receipt_was_not_recorded_replays_it_on_the_next_send()
    {
        var id = await SeedReview(1);
        var charge = new GatedCharge();
        charge.Release();
        var claims = TestDb.NewClaims();
        var loaded = await Stored(id);
        var fingerprint = SnapshotFingerprint.Of(Orders().Serialize(loaded));
        var won = (ClaimResult.Won)
            await claims.TryClaim(EffectKey(id), TimeSpan.FromMinutes(5), fingerprint);
        (await claims.Complete(EffectKey(id), won.OwnerToken, "receipt-earlier")).Should().BeTrue();

        var send = await Runner(charge).Run(User, id, "req-1");

        send.Should().BeOfType<AdvanceOutcome.Advanced>().Which.Snapshot.Context["receipt"]!
            .GetValue<string>()
            .Should()
            .Be("receipt-earlier");
        charge.Calls.Should().Be(0, Adr);
    }

    [Test]
    public async Task A_claim_recorded_without_a_fingerprint_replays_its_receipt_as_before()
    {
        var id = await SeedReview(1, 2, 3);
        var charge = new GatedCharge();
        charge.Release();
        var claims = TestDb.NewClaims();
        var won = (ClaimResult.Won)await claims.TryClaim(EffectKey(id), TimeSpan.FromMinutes(5));
        (await claims.Complete(EffectKey(id), won.OwnerToken, "receipt-legacy")).Should().BeTrue();

        var send = await Runner(charge).Run(User, id, "req-1");

        send.Should().BeOfType<AdvanceOutcome.Advanced>().Which.Snapshot.Context["receipt"]!
            .GetValue<string>()
            .Should()
            .Be("receipt-legacy");
        charge.Calls.Should().Be(0, Adr);
    }

    [Test]
    public async Task The_claim_records_the_fingerprint_of_the_content_the_effect_ran_on()
    {
        var id = await SeedReview(4);
        var charge = new GatedCharge();
        charge.Release();
        var expected = SnapshotFingerprint.Of(Orders().Serialize(await Stored(id)));

        (await Runner(charge).Run(User, id, "req-1")).Should().BeOfType<AdvanceOutcome.Advanced>();

        var completed = await TestDb.NewClaims().GetCompleted(EffectKey(id));
        completed.Should().NotBeNull();
        completed!.Receipt.Should().Be("receipt-1");
        completed.ContentFingerprint.Should().Be(expected, Adr);
    }

    [Test]
    public async Task A_reset_while_the_effect_runs_does_not_let_it_run_again()
    {
        var id = await SeedReview(1);
        var charge = new GatedCharge();

        var send = Task.Run(() => Runner(charge).Run(User, id, "req-1"));
        await charge.Entered;
        (await Orders().Autosave(User, id, DraftSnapshot))
            .Should()
            .BeOfType<AutosaveResult.Saved>();
        charge.Release();
        (await send).Should().BeOfType<AdvanceOutcome.Conflict>(Adr);

        // A second reset, after the effect finished, still leaves its unrecorded receipt with the claim.
        (await Orders().Autosave(User, id, DraftSnapshot))
            .Should()
            .BeOfType<AutosaveResult.Saved>();
        await Orders().Autosave(User, id, OrderMachine.ReviewSnapshot(1));
        var resend = await Runner(charge).Run(User, id, "req-2");

        resend.Should().BeOfType<AdvanceOutcome.Advanced>();
        charge.Calls.Should().Be(1, Adr);
        (await Stored(id)).Context["receipt"]!.GetValue<string>().Should().Be("receipt-1");
    }

    [Test]
    public async Task A_reset_after_the_order_is_placed_lets_the_next_order_run_its_effect()
    {
        var id = await SeedReview(1);
        var charge = new GatedCharge();
        charge.Release();
        (await Runner(charge).Run(User, id, "req-1")).Should().BeOfType<AdvanceOutcome.Advanced>();

        (await Orders().Advance(User, id, "Reset")).Should().BeOfType<AdvanceOutcome.Advanced>();
        await Orders().Autosave(User, id, OrderMachine.ReviewSnapshot(7));
        var next = await Runner(charge).Run(User, id, "req-2");

        next.Should().BeOfType<AdvanceOutcome.Advanced>().Which.Snapshot.Context["receipt"]!
            .GetValue<string>()
            .Should()
            .Be("receipt-2");
        charge.Calls.Should().Be(2);
    }

    [Test]
    public async Task An_effect_cancelled_after_charging_does_not_run_again()
    {
        var id = await SeedReview(1);
        var charge = new GatedCharge { CancelAfterCharging = true };
        charge.Release();

        var first = async () => await Runner(charge).Run(User, id, "req-1");
        await first.Should().ThrowAsync<OperationCanceledException>();

        charge.CancelAfterCharging = false;
        var second = await Runner(charge).Run(User, id, "req-2");

        second
            .Should()
            .BeOfType<AdvanceOutcome.Rejected>()
            .Which.Reason.Should()
            .Be("effect-in-progress", Adr);
        charge.Calls.Should().Be(1, Adr);
    }

    [Test]
    public async Task The_effect_is_not_handed_the_requests_cancellation()
    {
        var id = await SeedReview(1);
        var charge = new GatedCharge();
        charge.Release();
        using var request = new CancellationTokenSource();

        await Runner(charge).Run(User, id, "req-1", request.Token);

        charge.SawCancellableToken.Should().BeFalse(Adr);
    }

    [Test]
    public async Task Ttl_expiry_cancelled_midway_leaves_no_orphan_claim()
    {
        var id = await SeedReview(1);
        var claims = TestDb.NewClaims();
        var won = (ClaimResult.Won)await claims.TryClaim(EffectKey(id), TimeSpan.FromMinutes(5));
        (await claims.Complete(EffectKey(id), won.OwnerToken, "receipt-old")).Should().BeTrue();
        await TestDb.BackdateDraft(User, id, DateTimeOffset.UtcNow.AddDays(-2));

        using var request = new CancellationTokenSource();
        var service = new OrderMachine().CreateService(
            new CancellingStore(TestDb.NewStore(), request),
            new CancellingClaims(TestDb.NewClaims(), request),
            draftTtl: TimeSpan.FromHours(1)
        );
        try
        {
            await service.Load(User, id, request.Token);
        }
        catch (OperationCanceledException)
        {
            // The request went away midway; what matters is what it left behind.
        }

        (await Orders().Load(User, id)).Should().BeOfType<LoadResult.NotFound>();
        (await TestDb.NewClaims().TryClaim(EffectKey(id), TimeSpan.FromMinutes(5)))
            .Should()
            .BeOfType<ClaimResult.Won>(Adr);
    }

    [Test]
    public async Task Send_reports_an_effect_that_timed_out_as_a_failed_delivery()
    {
        var registry = new SnapshotMachineRegistry(
            new IMachine[] { new OrderMachine() },
            TestDb.NewStore(),
            TestDb.NewClaims(),
            new IdempotentEffect(TestDb.NewClaims()),
            new ServiceCollection()
                .AddSingleton<IOrderCharge>(new TimingOutCharge())
                .BuildServiceProvider()
        );
        var id = await SeedReview(1);

        var send = await new SendSnapshotJunction(registry, new FakePrincipal(User)).Run(
            new SendSnapshotInput { Machine = "order", Id = id }
        );

        send.Problem!.Code.Should().Be("delivery-failed");
    }

    [Test]
    public async Task Autosave_of_a_new_draft_does_not_overwrite_one_created_after_its_read()
    {
        var id = Guid.NewGuid();
        (await TestDb.NewStore().Upsert(User, id, Parse(OrderPlaced("receipt-1", 1))))
            .Should()
            .BeTrue();
        var service = new OrderMachine().CreateService(
            new ReadsNothingStore(TestDb.NewStore()),
            TestDb.NewClaims()
        );

        (await service.Autosave(User, id, OrderMachine.ReviewSnapshot(9)))
            .Should()
            .BeOfType<AutosaveResult.Conflict>(Adr);
        (await Stored(id)).State.Should().Be("Placed", Adr);
    }

    [Test]
    public async Task A_store_keyed_without_the_machine_keeps_another_machines_draft()
    {
        var id = Guid.NewGuid();
        var store = new MachineLessStore();
        var orders = new OrderMachine().CreateService(store, claims: null);
        (await store.Upsert(User, id, Parse(OrderPlaced("receipt-1", 1)))).Should().BeTrue();

        var turnstile = new TurnstileMachine().CreateService(store, claims: null);
        var locked = new JsonObject
        {
            ["machine"] = "turnstile",
            ["version"] = 1,
            ["state"] = "Locked",
            ["context"] = new JsonObject(),
        }.ToJsonString();
        var ship = new ShipMachine().CreateService(store, claims: null);
        var packing = new JsonObject
        {
            ["machine"] = "ship",
            ["version"] = 1,
            ["state"] = "Packing",
            ["context"] = new JsonObject(),
        }.ToJsonString();

        (await turnstile.Autosave(User, id, locked)).Should().NotBeOfType<AutosaveResult.Saved>();
        (await ship.Autosave(User, id, packing)).Should().NotBeOfType<AutosaveResult.Saved>();
        (await orders.Load(User, id))
            .Should()
            .BeOfType<LoadResult.Loaded>()
            .Which.Snapshot.State.Should()
            .Be("Placed");
    }

    private static string OrderPlaced(string receipt, params int[] items)
    {
        var array = new JsonArray();
        foreach (var item in items)
            array.Add(item);
        return new JsonObject
        {
            ["machine"] = "order",
            ["version"] = 1,
            ["state"] = "Placed",
            ["context"] = new JsonObject { ["items"] = array, ["receipt"] = receipt },
        }.ToJsonString();
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

    /// <summary>A charge that holds until released, and records what it charged and the token it was given.</summary>
    private sealed class GatedCharge : IOrderCharge
    {
        private readonly TaskCompletionSource _gate = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource _entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);
        public List<int> ChargedItemCounts { get; } = [];
        public bool SawCancellableToken { get; private set; }
        public bool CancelAfterCharging { get; set; }
        public Task Entered => _entered.Task;

        public void Release() => _gate.TrySetResult();

        public async Task<string> Run(
            Snapshot snapshot,
            CancellationToken cancellationToken = default
        )
        {
            SawCancellableToken |= cancellationToken.CanBeCanceled;
            var n = Interlocked.Increment(ref _calls);
            lock (ChargedItemCounts)
                ChargedItemCounts.Add(ItemCount(snapshot));
            _entered.TrySetResult();
            await _gate.Task;
            if (CancelAfterCharging)
                throw new OperationCanceledException("the caller went away after the charge");
            return $"receipt-{n}";
        }
    }

    /// <summary>An effect whose outbound call timed out: HttpClient reports that as a cancellation.</summary>
    private sealed class TimingOutCharge : IOrderCharge
    {
        public Task<string> Run(Snapshot snapshot, CancellationToken cancellationToken = default) =>
            throw new TaskCanceledException(
                "The request was canceled due to the configured timeout."
            );
    }

    /// <summary>A store that answers every machine-scoped read with nothing, as if the draft did not exist yet.</summary>
    private sealed class ReadsNothingStore(EfSnapshotStore inner) : ISnapshotStore
    {
        public Task<StoredSnapshot?> Get(
            string userKey,
            Guid id,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<StoredSnapshot?>(null);

        public Task<StoredSnapshot?> Get(
            string userKey,
            string machine,
            Guid id,
            CancellationToken cancellationToken = default
        ) => Task.FromResult<StoredSnapshot?>(null);

        public Task Delete(
            string userKey,
            Guid id,
            CancellationToken cancellationToken = default
        ) => inner.Delete(userKey, id, cancellationToken);

        public Task<bool> Upsert(
            string userKey,
            Guid id,
            Snapshot snapshot,
            CancellationToken cancellationToken = default
        ) => inner.Upsert(userKey, id, snapshot, cancellationToken);

        public Task<bool> Update(
            string userKey,
            Guid id,
            Snapshot snapshot,
            Guid expectedToken,
            string? requestId = null,
            CancellationToken cancellationToken = default
        ) => inner.Update(userKey, id, snapshot, expectedToken, requestId, cancellationToken);

        public Task<bool> Insert(
            string userKey,
            Guid id,
            Snapshot snapshot,
            CancellationToken cancellationToken = default
        ) => inner.Insert(userKey, id, snapshot, cancellationToken);
    }

    /// <summary>A store that cancels the request as it deletes a draft, then deletes it on the token it was given.</summary>
    private sealed class CancellingStore(EfSnapshotStore inner, CancellationTokenSource request)
        : ISnapshotStore
    {
        public Task<StoredSnapshot?> Get(
            string userKey,
            Guid id,
            CancellationToken cancellationToken = default
        ) => inner.Get(userKey, id, cancellationToken);

        public Task<StoredSnapshot?> Get(
            string userKey,
            string machine,
            Guid id,
            CancellationToken cancellationToken = default
        ) => inner.Get(userKey, machine, id, cancellationToken);

        public Task Delete(
            string userKey,
            Guid id,
            CancellationToken cancellationToken = default
        ) => inner.Delete(userKey, id, cancellationToken);

        public async Task Delete(
            string userKey,
            string machine,
            Guid id,
            CancellationToken cancellationToken = default
        )
        {
            await inner.Delete(userKey, machine, id, cancellationToken);
            await request.CancelAsync();
        }

        public Task<bool> Upsert(
            string userKey,
            Guid id,
            Snapshot snapshot,
            CancellationToken cancellationToken = default
        ) => inner.Upsert(userKey, id, snapshot, cancellationToken);

        public Task<bool> Update(
            string userKey,
            Guid id,
            Snapshot snapshot,
            Guid expectedToken,
            string? requestId = null,
            CancellationToken cancellationToken = default
        ) => inner.Update(userKey, id, snapshot, expectedToken, requestId, cancellationToken);
    }

    /// <summary>A claim ledger that cancels the request as a release starts, then releases on the token it was given.</summary>
    private sealed class CancellingClaims(EfEffectClaimStore inner, CancellationTokenSource request)
        : IEffectClaimStore
    {
        public Task<ClaimResult> TryClaim(
            string effectKey,
            TimeSpan lease,
            CancellationToken cancellationToken = default
        ) => inner.TryClaim(effectKey, lease, cancellationToken);

        public Task<bool> Complete(
            string effectKey,
            Guid ownerToken,
            string receipt,
            CancellationToken cancellationToken = default
        ) => inner.Complete(effectKey, ownerToken, receipt, cancellationToken);

        public Task<string?> GetReceipt(
            string effectKey,
            CancellationToken cancellationToken = default
        ) => inner.GetReceipt(effectKey, cancellationToken);

        public async Task Release(string effectKey, CancellationToken cancellationToken = default)
        {
            await request.CancelAsync();
            await inner.Release(effectKey, cancellationToken);
        }

        public Task<bool> ReleaseOwned(
            string effectKey,
            Guid ownerToken,
            CancellationToken cancellationToken = default
        ) => inner.ReleaseOwned(effectKey, ownerToken, cancellationToken);

        public Task<int> ReclaimStale(
            DateTimeOffset cutoff,
            CancellationToken cancellationToken = default
        ) => inner.ReclaimStale(cutoff, cancellationToken);
    }

    /// <summary>An in-memory store written before drafts were keyed by machine: one draft per user and id.</summary>
    private sealed class MachineLessStore : ISnapshotStore
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
                Json(snapshot),
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
                Json(snapshot),
                Guid.NewGuid(),
                requestId,
                DateTimeOffset.UtcNow
            );
            return Task.FromResult(true);
        }

        private static string Json(Snapshot snapshot) =>
            new JsonObject
            {
                ["machine"] = snapshot.Machine,
                ["version"] = snapshot.Version,
                ["state"] = snapshot.State,
                ["context"] = snapshot.Context.DeepClone(),
            }.ToJsonString();
    }
}
