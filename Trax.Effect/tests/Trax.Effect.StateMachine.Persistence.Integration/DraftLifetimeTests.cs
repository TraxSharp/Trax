using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// <see cref="StateMachineOptions.MaxDraftLifetime"/> bounds a user's draft, and what it deletes: an instance in a
/// state only the server puts it in is kept as the record of what the server did (the idle TTL keeps it too), a draft
/// waiting on an invoked run is deleted with its run cancelled, a row with no creation time counts its age from its
/// last write, and a draft an effect is running on is kept until the effect settles. A TTL longer than the lifetime refuses to start.
/// </summary>
[TestFixture(StoreProvider.Postgres)]
[TestFixture(StoreProvider.Sqlite)]
[Property("adr", Adr)]
public class DraftLifetimeTests(StoreProvider provider)
{
    private const string Adr =
        "Trax.Docs/adr/0048-a-machine-is-served-as-a-typed-api-and-the-server-applies-every-transition.md";

    private static readonly TimeSpan PastLifetime =
        StateMachineOptions.DefaultMaxDraftLifetime + TimeSpan.FromDays(1);

    private static string NewUser() => $"u-{Guid.NewGuid():N}";

    // A host whose drafts expire by the default lifetime only: no idle TTL decides anything here.
    private InstanceHost Host(IInvokedRunCancellation? runs = null) =>
        InstanceHost.Create(provider, draftTtl: null, runCancellation: runs);

    private static DraftSweeper Sweeper(InstanceHost host) =>
        host.Services.GetRequiredService<DraftSweeper>();

    private static async Task<Guid> Seed(
        InstanceHost host,
        string user,
        string machine,
        string state,
        JsonObject context
    )
    {
        var id = Guid.NewGuid();
        await host.Seed(
            user,
            id,
            new Snapshot
            {
                Machine = machine,
                Version = 1,
                State = state,
                Context = context,
            }
        );
        return id;
    }

    private static async Task Born(InstanceHost host, Guid id, TimeSpan? ago)
    {
        using var scope = host.Scope();
        DateTimeOffset? at = ago is { } a ? DateTimeOffset.UtcNow - a : null;
        await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .SnapshotDrafts.Where(d => d.Id == id)
            .ExecuteUpdateAsync(x => x.SetProperty(d => d.CreatedAt, at));
    }

    private static IEnumerable<TestCaseData> KeptStates()
    {
        yield return new TestCaseData(
            LedgerMachine.Id,
            nameof(LedgerState.Closed),
            new JsonObject { ["entries"] = new JsonArray(1) }
        ).SetName("{m}(committed)");
        yield return new TestCaseData(
            "ship",
            nameof(ShipState.Shipped),
            new JsonObject { ["receipt"] = "r-1" }
        ).SetName("{m}(effect target)");
        yield return new TestCaseData(
            RuledBuildMachine.Id,
            nameof(BuildState.Built),
            new JsonObject { ["source"] = "repo", ["artifact"] = "a-1" }
        ).SetName("{m}(outcome target)");
        yield return new TestCaseData(
            OperatorApprovalMachine.Id,
            nameof(ApprovalState.Approved),
            new JsonObject { ["approvedBy"] = "operator" }
        ).SetName("{m}(gated event target)");
    }

    [TestCaseSource(nameof(KeptStates))]
    public async Task The_lifetime_keeps_an_instance_only_the_server_moved(
        string machine,
        string state,
        JsonObject context
    )
    {
        using var host = Host();
        var user = NewUser();
        var kept = await Seed(host, user, machine, state, context);
        var editable = await Seed(
            host,
            user,
            LedgerMachine.Id,
            nameof(LedgerState.Open),
            new JsonObject { ["entries"] = new JsonArray() }
        );
        await Born(host, kept, PastLifetime);
        await Born(host, editable, PastLifetime);

        await Sweeper(host).SweepOnce();

        (await host.Rows(kept))
            .Should()
            .ContainSingle(
                $"an instance in {state} is the record of what the server did, and the host's to keep. See {Adr}"
            );
        (await host.Rows(editable)).Should().BeEmpty("an editable draft is still bounded");
    }

    [TestCaseSource(nameof(KeptStates))]
    public async Task The_idle_ttl_keeps_an_instance_only_the_server_moved(
        string machine,
        string state,
        JsonObject context
    )
    {
        using var host = InstanceHost.Create(provider, draftTtl: TimeSpan.FromMinutes(1));
        var user = NewUser();
        var kept = await Seed(host, user, machine, state, context);
        var editable = await Seed(
            host,
            user,
            LedgerMachine.Id,
            nameof(LedgerState.Open),
            new JsonObject { ["entries"] = new JsonArray() }
        );
        var idleSince = DateTimeOffset.UtcNow.AddHours(-1);
        await host.Backdate(kept, SnapshotOwnerKind.User, idleSince);
        await host.Backdate(editable, SnapshotOwnerKind.User, idleSince);

        await Sweeper(host).SweepOnce();

        (await host.Rows(kept))
            .Should()
            .ContainSingle(
                $"an instance in {state} is the record of what the server did, however long it is idle. See {Adr}"
            );
        (await host.Rows(editable)).Should().BeEmpty("an idle editable draft still expires");
    }

    [Test]
    public async Task The_lifetime_deletes_a_draft_waiting_on_its_run_and_cancels_the_run()
    {
        var runs = new RecordingCancellation();
        using var host = Host(runs);
        var user = NewUser();
        var id = await Seed(
            host,
            user,
            RuledBuildMachine.Id,
            nameof(BuildState.Building),
            new JsonObject { ["source"] = "repo" }
        );
        var token = $"run-{Guid.NewGuid():N}";
        using (var scope = host.Scope())
        {
            var store = host.Instances(scope);
            var stored = await store.Get(DraftOwner.User(user), RuledBuildMachine.Id, id);
            (
                await store.SetInvokeToken(
                    DraftOwner.User(user),
                    RuledBuildMachine.Id,
                    id,
                    stored!.Token,
                    new InvokeTokenWrite(token)
                )
            )
                .Should()
                .BeTrue();
        }
        await Born(host, id, PastLifetime);
        runs.RowExists = () => host.Rows(id).Result.Count > 0;

        await Sweeper(host).SweepOnce();

        runs.Cancelled.Should()
            .Equal([token], $"a run that never ends no longer keeps its draft forever. See {Adr}");
        runs.RowExistedWhenCancelled.Should().Equal([true], "the run is cancelled first");
        (await host.Rows(id)).Should().BeEmpty();
    }

    [Test]
    public async Task A_row_without_a_creation_time_counts_its_age_from_its_last_write()
    {
        using var host = Host();
        var user = NewUser();
        var old = await Seed(
            host,
            user,
            LedgerMachine.Id,
            nameof(LedgerState.Open),
            new JsonObject { ["entries"] = new JsonArray() }
        );
        var recent = await Seed(
            host,
            user,
            LedgerMachine.Id,
            nameof(LedgerState.Open),
            new JsonObject { ["entries"] = new JsonArray() }
        );
        await Born(host, old, ago: null);
        await Born(host, recent, ago: null);
        await host.Backdate(old, SnapshotOwnerKind.User, DateTimeOffset.UtcNow - PastLifetime);

        await Sweeper(host).SweepOnce();

        (await host.Rows(old))
            .Should()
            .BeEmpty($"a row with no created_at is still bounded. See {Adr}");
        (await host.Rows(recent)).Should().ContainSingle();
    }

    [Test]
    public async Task The_sweep_keeps_a_draft_an_effect_is_running_on_until_the_effect_settles()
    {
        using var host = Host();
        var user = NewUser();
        var id = await Seed(host, user, "ship", nameof(ShipState.Packing), new JsonObject());
        await Born(host, id, PastLifetime);
        var key = Key(host, "ship", user, id);
        Guid owner;
        using (var scope = host.Scope())
            owner = (
                (ClaimResult.Won)await Claims(scope).TryClaim(key, TimeSpan.FromMinutes(5), null)
            ).OwnerToken;

        try
        {
            await Sweeper(host).SweepOnce();
            (await host.Rows(id))
                .Should()
                .ContainSingle(
                    $"a draft whose effect is in flight is not deleted under it. See {Adr}"
                );
            (await ClaimRow(host, key)).Should().NotBeNull("the live claim is left as it was");
        }
        finally
        {
            using var scope = host.Scope();
            await Claims(scope).ReleaseOwned(key, owner);
        }

        await Sweeper(host).SweepOnce();
        (await host.Rows(id)).Should().BeEmpty("once the effect settled, the draft goes");
        (await ClaimRow(host, key)).Should().BeNull("the hold the sweep took goes with the draft");
    }

    [Test]
    public async Task The_sweep_releases_a_settled_claim_with_the_draft()
    {
        using var host = Host();
        var user = NewUser();
        var id = await Seed(host, user, "ship", nameof(ShipState.Packing), new JsonObject());
        await Born(host, id, PastLifetime);
        var key = Key(host, "ship", user, id);
        using (var scope = host.Scope())
            // A claim whose lease has passed with no receipt: whoever held it is gone.
            (await Claims(scope).TryClaim(key, TimeSpan.FromMinutes(-1), null))
                .Should()
                .BeOfType<ClaimResult.Won>();

        await Sweeper(host).SweepOnce();

        (await host.Rows(id)).Should().BeEmpty();
        (await ClaimRow(host, key))
            .Should()
            .BeNull(
                "a settled claim is released with its draft, so the next one under the id starts clean"
            );
    }

    [Test]
    public void A_ttl_longer_than_the_lifetime_refuses_to_start()
    {
        var start = () =>
            InstanceHost.Create(
                provider,
                draftTtl: StateMachineOptions.DefaultMaxDraftLifetime + TimeSpan.FromDays(1)
            );

        start
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*DraftTtl*longer than*MaxDraftLifetime*");
    }

    private static string Key(InstanceHost host, string machine, string user, Guid id) =>
        host
            .Services.GetServices<IMachine>()
            .OfType<IMachineInternals>()
            .Single(m => ((IMachine)m).Name == machine)
            .EffectKeys(user, id)
            .Single();

    private static IEffectClaimStore Claims(IServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<IEffectClaimStore>();

    private static async Task<Trax.Effect.Models.EffectClaim.EffectClaim?> ClaimRow(
        InstanceHost host,
        string key
    )
    {
        using var scope = host.Scope();
        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .EffectClaims.AsNoTracking()
            .FirstOrDefaultAsync(x => x.EffectKey == key);
    }

    /// <summary>Records each cancel, and whether the draft still existed when it was asked for.</summary>
    private sealed class RecordingCancellation : IInvokedRunCancellation
    {
        public List<string> Cancelled { get; } = [];
        public List<bool> RowExistedWhenCancelled { get; } = [];
        public Func<bool>? RowExists { get; set; }

        public Task Cancel(string invokeToken, CancellationToken cancellationToken = default)
        {
            Cancelled.Add(invokeToken);
            RowExistedWhenCancelled.Add(RowExists?.Invoke() ?? false);
            return Task.CompletedTask;
        }
    }
}
