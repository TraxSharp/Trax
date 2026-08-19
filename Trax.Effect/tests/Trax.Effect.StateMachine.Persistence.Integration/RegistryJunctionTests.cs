using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;
using Trax.Effect.StateMachine.Persistence.Mutations;

namespace Trax.Effect.StateMachine.Persistence.Integration;

/// <summary>
/// The FOUR generic mutations serve every registered machine via the registry (dispatch on the `machine`
/// discriminator). Invoked directly the way Trax tests junctions, against real Postgres.
/// </summary>
public class RegistryJunctionTests
{
    private static readonly ISnapshotPrincipal User = new FakePrincipal("u");

    private static ISnapshotMachineRegistry NewRegistry(IOrderCharge? effect = null)
    {
        var context = TestDb.NewContext();
        var provider = new ServiceCollection()
            .AddSingleton<IOrderCharge>(effect ?? new CountingEffect())
            .BuildServiceProvider();
        return new SnapshotMachineRegistry(
            new IMachine[]
            {
                new TurnstileMachine(),
                new OrderMachine(),
                new DeclarativeTurnstileMachine(),
            },
            new EfSnapshotStore(context),
            new EfEffectClaimStore(context),
            new IdempotentEffect(new EfEffectClaimStore(context)),
            provider
        );
    }

    [Test]
    public async Task Save_advance_and_load_dispatch_to_the_named_machine()
    {
        var id = Guid.NewGuid();

        var save = await new SaveSnapshotJunction(NewRegistry(), User).Run(
            new SaveSnapshotInput
            {
                Machine = "turnstile",
                Id = id,
                Snapshot = TestTurnstile.InitialJson,
            }
        );
        save.Problem.Should().BeNull();
        save.Snapshot.Should().Contain("\"state\":\"Locked\"");

        var advance = await new AdvanceSnapshotJunction(NewRegistry(), User).Run(
            new AdvanceSnapshotInput
            {
                Machine = "turnstile",
                Id = id,
                Trigger = "Coin",
                Input = "{\"coin\":\"quarter\"}",
            }
        );
        advance.Snapshot.Should().Contain("\"state\":\"Unlocked\"");

        var load = await new LoadSnapshotJunction(NewRegistry(), User).Run(
            new LoadSnapshotInput { Machine = "turnstile", Id = id }
        );
        load.Snapshot.Should().Contain("\"state\":\"Unlocked\"");
    }

    [Test]
    public async Task Send_runs_a_machines_effect_exactly_once_and_a_retry_replays()
    {
        var id = Guid.NewGuid();
        var effect = new CountingEffect();

        (
            await new SaveSnapshotJunction(NewRegistry(), User).Run(
                new SaveSnapshotInput
                {
                    Machine = "order",
                    Id = id,
                    Snapshot = OrderMachine.ReviewSnapshot(1, 2),
                }
            )
        ).Problem.Should().BeNull();

        var send = await new SendSnapshotJunction(NewRegistry(effect), User).Run(
            new SendSnapshotInput
            {
                Machine = "order",
                Id = id,
                RequestId = "r1",
            }
        );
        send.Problem.Should().BeNull();
        send.Snapshot.Should().Contain("\"state\":\"Placed\"");
        effect.Calls.Should().Be(1);

        var retry = await new SendSnapshotJunction(NewRegistry(effect), User).Run(
            new SendSnapshotInput
            {
                Machine = "order",
                Id = id,
                RequestId = "r2",
            }
        );
        retry.Snapshot.Should().Contain("\"state\":\"Placed\"");
        effect.Calls.Should().Be(1);
    }

    [Test]
    public void SchemaHash_is_a_stable_hash_for_a_declarative_machine_and_null_for_a_raw_delegate_one()
    {
        var registry = NewRegistry();

        // A declarative machine has an exportable IR, so a stable 64-hex hash, surfaced by the registry.
        var hash = registry.SchemaHash("declarative-turnstile");
        hash.Should().NotBeNullOrEmpty();
        hash.Should()
            .MatchRegex("^[0-9a-f]{64}$", "it is a lowercase hex SHA-256 of the exported IR");
        new DeclarativeTurnstileMachine()
            .SchemaHash.Should()
            .Be(hash, "the hash is stable across builds");

        // A raw-delegate machine cannot export an IR, so it has NO schema hash and no handshake: null, not throw.
        registry.SchemaHash("turnstile").Should().BeNull();
        new TurnstileMachine().SchemaHash.Should().BeNull();

        registry.SchemaHash("nope").Should().BeNull();
    }

    private const string DeclarativeTurnstileLocked =
        "{\"machine\":\"declarative-turnstile\",\"version\":1,\"state\":\"Locked\",\"context\":{}}";

    private const string DeclarativeTurnstileUnlocked =
        "{\"machine\":\"declarative-turnstile\",\"version\":1,\"state\":\"Unlocked\",\"context\":{\"paidWith\":\"quarter\"}}";

    [Test]
    public async Task A_stale_client_schema_hash_is_refused_while_matching_absent_or_a_hashless_machine_is_not()
    {
        const string Stale = "0000000000000000000000000000000000000000000000000000000000000000";
        var server = NewRegistry().SchemaHash("declarative-turnstile")!;

        // Save/advance/load refuse a client whose hash differs from the server's (a declarative machine).
        (
            await new SaveSnapshotJunction(NewRegistry(), User).Run(
                new SaveSnapshotInput
                {
                    Machine = "declarative-turnstile",
                    Id = Guid.NewGuid(),
                    Snapshot = DeclarativeTurnstileLocked,
                    SchemaHash = Stale,
                }
            )
        )
            .Problem!.Code.Should()
            .Be("schema-mismatch");
        (
            await new AdvanceSnapshotJunction(NewRegistry(), User).Run(
                new AdvanceSnapshotInput
                {
                    Machine = "declarative-turnstile",
                    Id = Guid.NewGuid(),
                    Trigger = "Coin",
                    SchemaHash = Stale,
                }
            )
        ).Problem!.Code.Should().Be("schema-mismatch");
        (
            await new LoadSnapshotJunction(NewRegistry(), User).Run(
                new LoadSnapshotInput
                {
                    Machine = "declarative-turnstile",
                    Id = Guid.NewGuid(),
                    SchemaHash = Stale,
                }
            )
        ).Problem!.Code.Should().Be("schema-mismatch");

        // The matching hash passes the guard, so the save then succeeds; and omitting it skips the check.
        (
            await new SaveSnapshotJunction(NewRegistry(), User).Run(
                new SaveSnapshotInput
                {
                    Machine = "declarative-turnstile",
                    Id = Guid.NewGuid(),
                    Snapshot = DeclarativeTurnstileLocked,
                    SchemaHash = server,
                }
            )
        )
            .Problem.Should()
            .BeNull();
        (
            await new SaveSnapshotJunction(NewRegistry(), User).Run(
                new SaveSnapshotInput
                {
                    Machine = "declarative-turnstile",
                    Id = Guid.NewGuid(),
                    Snapshot = DeclarativeTurnstileLocked,
                }
            )
        ).Problem.Should().BeNull();

        // A raw-delegate machine has a null server hash, so the guard SKIPS even a stale client hash (it falls
        // through to the normal flow) rather than throwing — advance reaches "not-found", NOT schema-mismatch.
        (
            await new AdvanceSnapshotJunction(NewRegistry(), User).Run(
                new AdvanceSnapshotInput
                {
                    Machine = "turnstile",
                    Id = Guid.NewGuid(),
                    Trigger = "Coin",
                    Input = "{\"coin\":\"quarter\"}",
                    SchemaHash = Stale,
                }
            )
        )
            .Problem!.Code.Should()
            .Be("not-found");
    }

    [Test]
    public async Task Advance_refuses_a_divergent_client_result_and_accepts_a_matching_or_absent_one()
    {
        async Task Save(Guid id) =>
            await new SaveSnapshotJunction(NewRegistry(), User).Run(
                new SaveSnapshotInput
                {
                    Machine = "declarative-turnstile",
                    Id = id,
                    Snapshot = DeclarativeTurnstileLocked,
                }
            );
        Task<AdvanceSnapshotOutput> Advance(Guid id, string? clientResult) =>
            new AdvanceSnapshotJunction(NewRegistry(), User).Run(
                new AdvanceSnapshotInput
                {
                    Machine = "declarative-turnstile",
                    Id = id,
                    Trigger = "Coin",
                    Input = "{\"coin\":\"quarter\"}",
                    ClientResult = clientResult,
                }
            );

        // The client's twin computed the same result the server did: the advance succeeds and returns it.
        var idOk = Guid.NewGuid();
        await Save(idOk);
        var ok = await Advance(idOk, DeclarativeTurnstileUnlocked);
        ok.Problem.Should().BeNull();
        ok.Snapshot.Should().Contain("\"state\":\"Unlocked\"");

        // The client claimed a different result: refused as a divergence (the server result stays authoritative).
        var idBad = Guid.NewGuid();
        await Save(idBad);
        (await Advance(idBad, DeclarativeTurnstileLocked))
            .Problem!.Code.Should()
            .Be("client-divergence");

        // No client result: no check (older clients that don't send it are unaffected).
        var idNone = Guid.NewGuid();
        await Save(idNone);
        (await Advance(idNone, null)).Problem.Should().BeNull();
    }

    [Test]
    public async Task An_unknown_machine_is_a_typed_problem()
    {
        var output = await new SaveSnapshotJunction(NewRegistry(), User).Run(
            new SaveSnapshotInput
            {
                Machine = "nope",
                Id = Guid.NewGuid(),
                Snapshot = TestTurnstile.InitialJson,
            }
        );

        output.Problem!.Code.Should().Be("unknown-machine");
    }

    [Test]
    public async Task Send_to_a_machine_with_no_effect_is_refused()
    {
        var output = await new SendSnapshotJunction(NewRegistry(), User).Run(
            new SendSnapshotInput { Machine = "turnstile", Id = Guid.NewGuid() }
        );

        output.Problem!.Code.Should().Be("no-effect");
    }

    [Test]
    public async Task An_unauthenticated_request_is_a_typed_problem()
    {
        var anon = new FakePrincipal(null);

        var output = await new SaveSnapshotJunction(NewRegistry(), anon).Run(
            new SaveSnapshotInput
            {
                Machine = "turnstile",
                Id = Guid.NewGuid(),
                Snapshot = TestTurnstile.InitialJson,
            }
        );

        output.Problem!.Code.Should().Be("unauthenticated");
    }

    private const string DraftOrder =
        "{\"machine\":\"order\",\"version\":1,\"state\":\"Draft\",\"context\":{\"items\":[],\"receipt\":null}}";

    [Test]
    public async Task Save_of_an_invalid_snapshot_is_a_typed_rejection()
    {
        var invalid =
            "{\"machine\":\"turnstile\",\"version\":1,\"state\":\"Unlocked\",\"context\":{}}";

        var output = await new SaveSnapshotJunction(NewRegistry(), User).Run(
            new SaveSnapshotInput
            {
                Machine = "turnstile",
                Id = Guid.NewGuid(),
                Snapshot = invalid,
            }
        );

        output.Problem!.Code.Should().Be("invalid-context");
    }

    [Test]
    public async Task Advance_reports_unknown_machine_missing_draft_malformed_input_and_illegal_trigger()
    {
        var advance = (AdvanceSnapshotInput input) =>
            new AdvanceSnapshotJunction(NewRegistry(), User).Run(input);

        (
            await advance(
                new()
                {
                    Machine = "nope",
                    Id = Guid.NewGuid(),
                    Trigger = "Coin",
                }
            )
        ).Problem!.Code.Should().Be("unknown-machine");
        (
            await advance(
                new()
                {
                    Machine = "turnstile",
                    Id = Guid.NewGuid(),
                    Trigger = "Coin",
                    Input = "{\"coin\":\"quarter\"}",
                }
            )
        ).Problem!.Code.Should().Be("not-found");

        var id = Guid.NewGuid();
        await new SaveSnapshotJunction(NewRegistry(), User).Run(
            new SaveSnapshotInput
            {
                Machine = "turnstile",
                Id = id,
                Snapshot = TestTurnstile.InitialJson,
            }
        );
        (
            await advance(
                new()
                {
                    Machine = "turnstile",
                    Id = id,
                    Trigger = "Coin",
                    Input = "{bad json",
                }
            )
        ).Problem!.Code.Should().Be("malformed");
        (
            await advance(
                new()
                {
                    Machine = "turnstile",
                    Id = id,
                    Trigger = "Push",
                }
            )
        ).Problem!.Code.Should().Be("no-transition");
    }

    [Test]
    public async Task Load_reports_unknown_machine_and_a_missing_draft()
    {
        (
            await new LoadSnapshotJunction(NewRegistry(), User).Run(
                new LoadSnapshotInput { Machine = "nope", Id = Guid.NewGuid() }
            )
        )
            .Problem!.Code.Should()
            .Be("unknown-machine");
        (
            await new LoadSnapshotJunction(NewRegistry(), User).Run(
                new LoadSnapshotInput { Machine = "turnstile", Id = Guid.NewGuid() }
            )
        )
            .Problem!.Code.Should()
            .Be("not-found");
    }

    [Test]
    public async Task Send_reports_unknown_machine_missing_draft_wrong_state_and_delivery_failure()
    {
        (
            await new SendSnapshotJunction(NewRegistry(), User).Run(
                new SendSnapshotInput { Machine = "nope", Id = Guid.NewGuid() }
            )
        )
            .Problem!.Code.Should()
            .Be("unknown-machine");
        (
            await new SendSnapshotJunction(NewRegistry(), User).Run(
                new SendSnapshotInput { Machine = "order", Id = Guid.NewGuid() }
            )
        )
            .Problem!.Code.Should()
            .Be("not-found");

        var draftId = Guid.NewGuid();
        await new SaveSnapshotJunction(NewRegistry(), User).Run(
            new SaveSnapshotInput
            {
                Machine = "order",
                Id = draftId,
                Snapshot = DraftOrder,
            }
        );
        (
            await new SendSnapshotJunction(NewRegistry(), User).Run(
                new SendSnapshotInput { Machine = "order", Id = draftId }
            )
        )
            .Problem!.Code.Should()
            .Be("no-transition");

        var reviewId = Guid.NewGuid();
        await new SaveSnapshotJunction(NewRegistry(), User).Run(
            new SaveSnapshotInput
            {
                Machine = "order",
                Id = reviewId,
                Snapshot = OrderMachine.ReviewSnapshot(1),
            }
        );
        (
            await new SendSnapshotJunction(NewRegistry(new CountingEffect(fail: true)), User).Run(
                new SendSnapshotInput { Machine = "order", Id = reviewId }
            )
        )
            .Problem!.Code.Should()
            .Be("delivery-failed");
    }
}
