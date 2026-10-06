using System.Text.Json.Nodes;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;
using static Trax.Effect.StateMachine.Rules;

namespace Trax.Cli.Tests.Fakes;

public enum CheckoutState
{
    Cart,
    Review,
    Paid,
}

public enum CheckoutTrigger
{
    Next,
    Back,
    Pay,
    Restart,
}

/// <summary>A marker for the checkout's one irreversible effect (resolved from DI in a real host).</summary>
public interface ICheckoutCharge : ISnapshotEffect;

/// <summary>
/// The multi-step, effectful checkout (Cart -> Review -> Paid) authored with the DECLARATIVE surface: a
/// multi-key context, count/compare guards, a guarded effect edge, and per-state invariants. It reproduces the
/// behavior of the hand-written twin (same guards, reducers, and validators on the enumerated cases), so its
/// generated artifacts drop in for the retired machine.json pipeline. Second machine after turnstile: this is
/// where multi-key context, count guards, and the exactly-once effect are exercised through the generator.
/// </summary>
public sealed class DeclarativeCheckoutMachine : Machine<CheckoutState, CheckoutTrigger>
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

    private static JsonObject FreshCart() =>
        new()
        {
            ["currency"] = "USD",
            ["items"] = new JsonArray(),
            ["receipt"] = null,
            ["total"] = 0,
        };

    protected override void Configure(IMachineBuilder<CheckoutState, CheckoutTrigger> m)
    {
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

        // Differential inputs, matching the retired machine.json: a receipt sample and the empty {} for Pay,
        // plus valid Review/Paid seeds (items/total arrive via autosave, so those states are not BFS-reachable).
        m.Differential(d =>
            d.Sample(CheckoutTrigger.Pay, new PayInput { Receipt = "rcpt_1" })
                .EmptySample(CheckoutTrigger.Pay)
                .Seed(
                    CheckoutState.Review,
                    new JsonObject
                    {
                        ["currency"] = "USD",
                        ["items"] = new JsonArray { "book" },
                        ["receipt"] = null,
                        ["total"] = 5,
                    }
                )
                .Seed(
                    CheckoutState.Paid,
                    new JsonObject
                    {
                        ["currency"] = "USD",
                        ["items"] = new JsonArray { "book" },
                        ["receipt"] = "rcpt_seed",
                        ["total"] = 5,
                    }
                )
        );
    }
}
