using System.Text.Json.Nodes;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.StateMachine.Persistence.Integration.Fakes;
using Trax.Effect.StateMachine.Persistence.Integration.Fixtures;
using Trax.Effect.StateMachine.Persistence.Mutations;

namespace Trax.Effect.StateMachine.Persistence.Integration;

public enum NoteState
{
    Draft,
}

public enum NoteTrigger
{
    Write,
}

/// <summary>A one-state machine whose only edge copies <c>input.text</c> into the context.</summary>
public sealed class NoteMachine : Machine<NoteState, NoteTrigger>
{
    protected override void Configure(IMachineBuilder<NoteState, NoteTrigger> m)
    {
        m.Id("note").Version(1).StartsAt(NoteState.Draft, () => new JsonObject());
        m.In(NoteState.Draft)
            .On(NoteTrigger.Write)
            .Reduce((_, input) => new JsonObject { ["text"] = input?["text"]?.DeepClone() })
            .To(NoteState.Draft);
    }
}

/// <summary>
/// Defects in the generic snapshot mutations, driven the way <see cref="RegistryJunctionTests"/> drives
/// them, against real Postgres.
/// </summary>
public class SnapshotMutationDefectTests
{
    private static readonly ISnapshotPrincipal User = new FakePrincipal("u");

    private static ISnapshotMachineRegistry NewRegistry(IOrderCharge? effect = null)
    {
        var context = TestDb.NewContext();
        var provider = new ServiceCollection()
            .AddSingleton<IOrderCharge>(effect ?? new CountingEffect())
            .BuildServiceProvider();
        return new SnapshotMachineRegistry(
            new IMachine[] { new TurnstileMachine(), new OrderMachine(), new NoteMachine() },
            new EfSnapshotStore(context),
            new EfEffectClaimStore(context),
            new IdempotentEffect(new EfEffectClaimStore(context)),
            provider
        );
    }

    private static Task<SaveSnapshotOutput> Save(string machine, Guid id, string snapshot) =>
        new SaveSnapshotJunction(NewRegistry(), User).Run(
            new SaveSnapshotInput
            {
                Machine = machine,
                Id = id,
                Snapshot = snapshot,
            }
        );

    private static Task<LoadSnapshotOutput> Load(string machine, Guid id) =>
        new LoadSnapshotJunction(NewRegistry(), User).Run(
            new LoadSnapshotInput { Machine = machine, Id = id }
        );

    [Test]
    public async Task A_client_divergence_refusal_must_not_have_persisted_the_advance()
    {
        var id = Guid.NewGuid();
        (await Save("turnstile", id, TestTurnstile.InitialJson)).Problem.Should().BeNull();

        var advance = await new AdvanceSnapshotJunction(NewRegistry(), User).Run(
            new AdvanceSnapshotInput
            {
                Machine = "turnstile",
                Id = id,
                Trigger = "Coin",
                Input = "{\"coin\":\"quarter\"}",
                ClientResult =
                    "{\"machine\":\"turnstile\",\"version\":1,\"state\":\"Locked\",\"context\":{}}",
            }
        );
        advance.Problem!.Code.Should().Be("client-divergence");

        // "the advance is refused" (runtime-integrity docs): the stored draft must still be Locked.
        (await Load("turnstile", id))
            .Snapshot.Should()
            .Contain("\"state\":\"Locked\"");
    }

    [Test]
    public async Task Advance_input_must_not_grow_a_draft_past_the_snapshot_limit()
    {
        var id = Guid.NewGuid();
        (
            await Save(
                "note",
                id,
                "{\"machine\":\"note\",\"version\":1,\"state\":\"Draft\",\"context\":{}}"
            )
        )
            .Problem.Should()
            .BeNull();

        var text = new string('x', 1024 * 1024);
        var advance = await new AdvanceSnapshotJunction(NewRegistry(), User).Run(
            new AdvanceSnapshotInput
            {
                Machine = "note",
                Id = id,
                Trigger = "Write",
                Input = new JsonObject { ["text"] = text }.ToJsonString(),
            }
        );

        // Autosave refuses a snapshot over SnapshotLimits.MaxSnapshotBytes before parsing it; the
        // authoritative path takes the same bytes through the trigger input and stores them.
        var stored = (await Load("note", id)).Snapshot ?? "";
        (advance.Problem is not null || stored.Length <= SnapshotLimits.MaxSnapshotBytes)
            .Should()
            .BeTrue(
                $"the stored draft is {stored.Length} bytes against a {SnapshotLimits.MaxSnapshotBytes}-byte limit"
            );
    }

    [Test]
    public async Task Autosave_of_a_NUL_character_must_return_a_problem_not_throw()
    {
        var id = Guid.NewGuid();
        var snapshot =
            "{\"machine\":\"turnstile\",\"version\":1,\"state\":\"Unlocked\",\"context\":{\"paidWith\":\"a\\u0000b\"}}";

        var save = async () => await Save("turnstile", id, snapshot);

        (await save.Should().NotThrowAsync()).Subject.Problem.Should().NotBeNull();
    }

    [Test]
    public async Task A_number_outside_double_range_must_not_wedge_the_draft()
    {
        var id = Guid.NewGuid();
        var snapshot =
            "{\"machine\":\"turnstile\",\"version\":1,\"state\":\"Unlocked\",\"context\":{\"paidWith\":\"q\",\"n\":1e400}}";

        // The save stores the row, then throws while serializing the reply.
        try
        {
            await Save("turnstile", id, snapshot);
        }
        catch (ArgumentException) { }

        var load = async () => await Load("turnstile", id);
        await load.Should()
            .NotThrowAsync("every later load of the draft must not fail on the stored value");
    }

    [Test]
    public async Task A_send_whose_request_id_an_earlier_advance_used_must_still_record_the_order()
    {
        var id = Guid.NewGuid();
        var effect = new CountingEffect();
        (
            await Save(
                "order",
                id,
                "{\"machine\":\"order\",\"version\":1,\"state\":\"Draft\",\"context\":{\"items\":[1],\"receipt\":null}}"
            )
        )
            .Problem.Should()
            .BeNull();

        // The client used the draft id as the idempotency key of an ordinary advance.
        (
            await new AdvanceSnapshotJunction(NewRegistry(), User).Run(
                new AdvanceSnapshotInput
                {
                    Machine = "order",
                    Id = id,
                    Trigger = "Next",
                    RequestId = id.ToString(),
                }
            )
        )
            .Snapshot.Should()
            .Contain("\"state\":\"Review\"");

        // A bare Send defaults its request id to the draft id. The effect runs, then the Place advance
        // "replays" the Next request and the receipt is dropped.
        var send = await new SendSnapshotJunction(NewRegistry(effect), User).Run(
            new SendSnapshotInput { Machine = "order", Id = id }
        );

        effect.Calls.Should().Be(1);
        send.Problem.Should().BeNull();
        (await Load("order", id)).Snapshot.Should().Contain("\"state\":\"Placed\"");
    }

    [Test]
    public async Task Autosave_must_store_the_canonical_state_name_not_an_alias()
    {
        var id = Guid.NewGuid();

        // "1" is OrderState.Review's numeric value; C# Enum.TryParse accepts it and the draft keeps the alias.
        var save = await Save(
            "order",
            id,
            "{\"machine\":\"order\",\"version\":1,\"state\":\"1\",\"context\":{\"items\":[1],\"receipt\":null}}"
        );

        if (save.Problem is null)
            (await Load("order", id)).Snapshot.Should().Contain("\"state\":\"Review\"");
    }
}
