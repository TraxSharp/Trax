using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using static Trax.Effect.StateMachine.Rules;

namespace Trax.Effect.StateMachine.Persistence.Integration.Fakes;

/// <summary>
/// The declarative turnstile as a system-owned machine, so a host's trusted advance through
/// <see cref="IMachineInstances"/> fires a trigger that declares its input.
/// </summary>
public sealed class SystemDeclarativeTurnstileMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public const string Id = "system-declarative-turnstile";

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m)
    {
        DeclarativeTurnstileMachine.ConfigureTurnstile(m, Id);
        m.SystemOwned();
    }
}

/// <summary>
/// A turnstile whose <c>Coin</c> requires the <c>Editor</c> role, for the refusal a client's rule produces.
/// </summary>
public sealed class GatedTurnstileMachine : Machine<TurnstileState, TurnstileTrigger>
{
    public const string Id = "gated-turnstile";

    protected override void Configure(IMachineBuilder<TurnstileState, TurnstileTrigger> m)
    {
        m.Id(Id).Version(1).StartsAt(TurnstileState.Locked, () => new JsonObject());

        m.In(TurnstileState.Locked)
            .Context()
            .On(TurnstileTrigger.Coin)
            .Requires("Editor")
            .To(TurnstileState.Unlocked);

        m.In(TurnstileState.Unlocked).Context().On(TurnstileTrigger.Push).To(TurnstileState.Locked);
    }

    /// <summary>A snapshot of this machine in <paramref name="state"/>, as the client sends it.</summary>
    public static string Json(string state) =>
        new JsonObject
        {
            ["machine"] = Id,
            ["version"] = 1,
            ["state"] = state,
            ["context"] = new JsonObject(),
        }.ToJsonString();
}

public enum ReceiptOrderState
{
    Review,
    Placed,
}

public enum ReceiptOrderTrigger
{
    Place,
    Reset,
}

/// <summary>
/// A declarative order whose effect-bound <c>Place</c> also declares its input: the effect runner fires it with the
/// receipt the effect returned, which the input record names.
/// </summary>
public sealed class ReceiptOrderMachine : Machine<ReceiptOrderState, ReceiptOrderTrigger>
{
    public const string Id = "receipt-order";

    public sealed record PlacedContext
    {
        [MinLength(1)]
        public string Receipt { get; init; } = "";
    }

    public sealed record PlaceInput
    {
        public string Receipt { get; init; } = "";
    }

    protected override void Configure(IMachineBuilder<ReceiptOrderState, ReceiptOrderTrigger> m)
    {
        m.Id(Id).Version(1).StartsAt(ReceiptOrderState.Review, () => new JsonObject());

        m.In(ReceiptOrderState.Review)
            .Context()
            .On(ReceiptOrderTrigger.Place)
            .WithInput<PlaceInput>()
            .RunsOnce<IOrderCharge>($"{Id}:place")
            .Reduce(Set((PlacedContext c) => c.Receipt).FromInput((PlaceInput i) => i.Receipt))
            .To(ReceiptOrderState.Placed);

        m.In(ReceiptOrderState.Placed)
            .Committed()
            .Context<PlacedContext>()
            .On(ReceiptOrderTrigger.Reset)
            .Reduce(Clear())
            .To(ReceiptOrderState.Review);
    }

    /// <summary>The initial snapshot, as the client sends it.</summary>
    public static string ReviewJson =>
        new JsonObject
        {
            ["machine"] = Id,
            ["version"] = 1,
            ["state"] = "Review",
            ["context"] = new JsonObject(),
        }.ToJsonString();
}
