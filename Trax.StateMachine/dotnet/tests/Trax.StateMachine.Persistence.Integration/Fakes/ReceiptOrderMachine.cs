using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

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
