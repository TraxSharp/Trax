using System.Text.Json.Nodes;
using static Trax.Effect.StateMachine.Rules;

namespace Trax.Effect.StateMachine.Tests.Fakes;

/// <summary>
/// The checkout authored with the declarative surface, as the committed <c>checkout.ir.json</c> the TypeScript
/// twin is generated from describes it. Unlike <see cref="TestCheckout"/>, whose delegates read the input
/// themselves, its <c>Pay</c> declares a <see cref="PayInput"/>, so the engine validates what is sent with it.
/// The conformance fixtures and the differential corpus replay against it.
/// </summary>
public static class DeclarativeCheckout
{
    public sealed record CartContext
    {
        public string Currency { get; init; } = "USD";
        public string[] Items { get; init; } = [];
        public string? Receipt { get; init; }
        public double Total { get; init; }
    }

    public sealed record PaidContext
    {
        public string Currency { get; init; } = "USD";
        public string[] Items { get; init; } = [];
        public string Receipt { get; init; } = "";
        public double Total { get; init; }
    }

    public sealed record PayInput
    {
        public string Receipt { get; init; } = "";
    }

    public static readonly BuiltMachine<CheckoutState, CheckoutTrigger> Built = Build();
    public static readonly SnapshotMachine<CheckoutState, CheckoutTrigger> Machine = Built.Engine;

    private static JsonObject FreshCart() =>
        new()
        {
            ["currency"] = "USD",
            ["items"] = new JsonArray(),
            ["receipt"] = null,
            ["total"] = 0,
        };

    private static BuiltMachine<CheckoutState, CheckoutTrigger> Build()
    {
        var m = new MachineBuilder<CheckoutState, CheckoutTrigger>();
        m.Id("checkout").Version(1).StartsAt(CheckoutState.Cart, FreshCart);

        m.In(CheckoutState.Cart)
            .Context<CartContext>()
            .Requires(Field((CartContext c) => c.Receipt).Absent())
            .On(CheckoutTrigger.Next)
            .When(Field((CartContext c) => c.Items).CountGreaterThan(0))
            .Because("Add an item before reviewing.")
            .To(CheckoutState.Review);

        m.In(CheckoutState.Review)
            .Context<CartContext>()
            .Requires(
                All(
                    Field((CartContext c) => c.Items).CountGreaterThan(0),
                    Field((CartContext c) => c.Total).GreaterThan(0),
                    Field((CartContext c) => c.Receipt).Absent()
                )
            )
            .On(CheckoutTrigger.Back)
            .To(CheckoutState.Cart)
            .On(CheckoutTrigger.Pay)
            .WithInput<PayInput>()
            .When(
                All(
                    Field((CartContext c) => c.Items).CountGreaterThan(0),
                    Field((CartContext c) => c.Total).GreaterThan(0),
                    Input((PayInput i) => i.Receipt).Present()
                )
            )
            .Because("A payable order needs items, a positive total, and a receipt.")
            .RunsOnce<ICheckoutCharge>("checkout:charge")
            .Reduce(Set((PaidContext c) => c.Receipt).FromInput((PayInput i) => i.Receipt))
            .To(CheckoutState.Paid);

        m.In(CheckoutState.Paid)
            .Context<PaidContext>()
            .Committed()
            .Requires(Field((PaidContext c) => c.Items).CountGreaterThan(0))
            .On(CheckoutTrigger.Restart)
            .Reduce(Reset())
            .To(CheckoutState.Cart);

        return m.Build();
    }
}
