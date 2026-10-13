using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Enums;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// Draft expiry belongs to the draft sweep: a draft idle past the configured TTL is deleted by the sweep, with its
/// effect claims, unless its state is one only the server puts it in, and a load never deletes one. Staleness is forced deterministically by backdating
/// <c>updated_at</c>, the analogue of the effect-claim tests' negative lease.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
[Property("adr", Adr)]
public class SnapshotDraftExpiryTests(StoreProvider provider)
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(1);

    private static string NewUser() => $"u-{Guid.NewGuid():N}";

    private static DateTimeOffset LongAgo => DateTimeOffset.UtcNow.AddHours(-1);

    private InstanceHost Host(TimeSpan? ttl, Action<IServiceCollection>? services = null) =>
        InstanceHost.Create(provider, draftTtl: ttl, services: services);

    private static DraftSweeper Sweeper(InstanceHost host) =>
        host.Services.GetRequiredService<DraftSweeper>();

    private static async Task<Guid> Saved(InstanceHost host, string user, string json)
    {
        var id = Guid.NewGuid();
        using var scope = host.Scope();
        (await host.Service(scope, "turnstile").Autosave(user, id, json))
            .Should()
            .BeOfType<AutosaveResult.Saved>();
        return id;
    }

    private static async Task<LoadResult> Load(
        InstanceHost host,
        string machine,
        string user,
        Guid id
    )
    {
        using var scope = host.Scope();
        return await host.Service(scope, machine).Load(user, id);
    }

    [Test]
    public async Task By_default_a_timed_draft_is_deleted_once_it_is_older_than_the_lifetime_bound()
    {
        using var host = Host(StateMachineOptions.DefaultDraftTtl);
        var user = NewUser();
        var old = Guid.NewGuid();
        var young = Guid.NewGuid();
        // Waiting is timed and reached by an ungated event: a state the lifetime bounds. (A gated event's target
        // would be kept past it as the record of what the server did.)
        await host.SeedDraft(user, old, PlainTimerMachine.Json("Waiting"));
        await host.SeedDraft(user, young, PlainTimerMachine.Json("Waiting"));
        (await RawSql.Timer(host, old))!
            .DueAt.Should()
            .NotBeNull("Waiting's timer keeps it past its TTL");
        await Born(host, old, StateMachineOptions.DefaultMaxDraftLifetime + TimeSpan.FromDays(1));
        await Born(host, young, StateMachineOptions.DefaultMaxDraftLifetime - TimeSpan.FromDays(1));

        await Sweeper(host).SweepOnce();

        (await host.Rows(old))
            .Should()
            .BeEmpty(
                $"a pending timer never keeps a draft past the default lifetime of 90 days. See {Adr}"
            );
        (await host.Rows(young)).Should().ContainSingle();
    }

    private static async Task Born(InstanceHost host, Guid id, TimeSpan ago)
    {
        using var scope = host.Scope();
        DateTimeOffset? at = DateTimeOffset.UtcNow - ago;
        await scope
            .ServiceProvider.GetRequiredService<Trax.Effect.Data.Services.DataContext.IDataContext>()
            .SnapshotDrafts.Where(d => d.Id == id)
            .ExecuteUpdateAsync(x => x.SetProperty(d => d.CreatedAt, at));
    }

    [Test]
    public async Task The_sweep_deletes_a_draft_idle_past_the_ttl()
    {
        using var host = Host(Ttl);
        var user = NewUser();
        var id = await Saved(host, user, TestTurnstile.UnlockedJson);
        await host.Backdate(id, SnapshotOwnerKind.User, LongAgo);

        await Sweeper(host).SweepOnce();

        (await host.Rows(id)).Should().BeEmpty($"the sweep deletes an expired draft. See {Adr}");
        (await Load(host, "turnstile", user, id)).Should().BeOfType<LoadResult.NotFound>();
    }

    [Test]
    [Property("adr", Adr)]
    public async Task Load_no_longer_deletes_an_expired_draft()
    {
        using var host = Host(Ttl);
        var user = NewUser();
        var id = await Saved(host, user, TestTurnstile.UnlockedJson);
        await host.Backdate(id, SnapshotOwnerKind.User, LongAgo);

        await Load(host, "turnstile", user, id);

        (await host.Rows(id))
            .Should()
            .ContainSingle(
                $"a read is a query: the sweeper deletes expired drafts, never a load. See {Adr}"
            );
    }

    [Test]
    public async Task The_sweep_keeps_a_draft_within_the_ttl()
    {
        using var host = Host(TimeSpan.FromMinutes(30));
        var user = NewUser();
        var id = await Saved(host, user, TestTurnstile.UnlockedJson);

        await Sweeper(host).SweepOnce();

        (await Load(host, "turnstile", user, id))
            .Should()
            .BeOfType<LoadResult.Loaded>()
            .Which.Snapshot.State.Should()
            .Be("Unlocked");
    }

    [Test]
    public async Task With_no_ttl_the_sweep_never_expires_a_draft()
    {
        using var host = Host(ttl: null);
        var user = NewUser();
        var id = await Saved(host, user, TestTurnstile.UnlockedJson);
        await host.Backdate(id, SnapshotOwnerKind.User, DateTimeOffset.UtcNow.AddYears(-1));

        await Sweeper(host).SweepOnce();

        (await Load(host, "turnstile", user, id)).Should().BeOfType<LoadResult.Loaded>();
    }

    [Test]
    public async Task A_placed_order_idle_past_the_ttl_is_kept_with_its_receipt()
    {
        var effect = new CountingEffect();
        using var host = Host(Ttl, s => s.AddSingleton<IOrderCharge>(effect));
        var user = NewUser();
        var id = Guid.NewGuid();

        await PlaceOrder(host, user, id);
        effect.Calls.Should().Be(1);

        // The placed order is the effect's target: the record of what the server did, which the TTL leaves alone
        // however long it is idle.
        await host.Backdate(id, SnapshotOwnerKind.User, LongAgo);
        await Sweeper(host).SweepOnce();

        var loaded = (await Load(host, "order", user, id))
            .Should()
            .BeOfType<LoadResult.Loaded>(
                $"an idle instance in a state only the server puts it in is the host's to keep. See {Adr}"
            )
            .Which;
        loaded.Snapshot.Context["receipt"]!.GetValue<string>().Should().Be("receipt-1");
        effect.Calls.Should().Be(1);
    }

    private static async Task<Snapshot> PlaceOrder(InstanceHost host, string user, Guid id)
    {
        // An order has an effect, so the generic save does not serve it: the review draft is seeded through the store.
        await host.Seed(user, id, TestDb.Parse(OrderMachine.ReviewSnapshot(1)));
        using var scope = host.Scope();
        var registry = scope.ServiceProvider.GetRequiredService<ISnapshotMachineRegistry>();
        return (await registry.EffectRunner("order")!.Run(user, id, $"send-{Guid.NewGuid()}"))
            .Should()
            .BeOfType<AdvanceOutcome.Advanced>()
            .Which.Snapshot;
    }
}
