using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trax.Effect.StateMachine.Tests.Fakes;

/// <summary>A marker for the checkout's one irreversible effect (resolved from DI in a real host).</summary>
public interface ICheckoutCharge { }

/// <summary>
/// The checkout machine authored with the FLUENT builder instead of a hand-written
/// <see cref="MachineDefinition{TState,TTrigger}"/>. Behavior is identical, so it drives the same shared
/// checkout fixtures. It also declares a committed state and an exactly-once effect inline, which the
/// build captures as metadata a host wires automatically.
/// </summary>
public static class FluentCheckout
{
    public static readonly BuiltMachine<CheckoutState, CheckoutTrigger> Built = Build();
    public static SnapshotMachine<CheckoutState, CheckoutTrigger> Machine => Built.Engine;

    private static int ItemsCount(JsonObject ctx) => ctx["items"] is JsonArray a ? a.Count : 0;

    private static bool ItemsIsArray(JsonObject ctx) => ctx["items"] is JsonArray;

    private static bool TotalIsNumber(JsonObject ctx) =>
        ctx["total"]?.GetValueKind() == JsonValueKind.Number;

    private static double Total(JsonObject ctx) =>
        ctx["total"]?.GetValueKind() == JsonValueKind.Number ? ctx["total"]!.GetValue<double>() : 0;

    private static bool ReceiptEmpty(JsonObject ctx) =>
        ctx["receipt"] is null || ctx["receipt"]!.GetValueKind() == JsonValueKind.Null;

    private static bool ReceiptPresent(JsonObject ctx) =>
        ctx["receipt"]?.GetValueKind() == JsonValueKind.String
        && ctx["receipt"]!.GetValue<string>().Length > 0;

    private static string? ReceiptInput(JsonNode? input) =>
        input is JsonObject o && o["receipt"]?.GetValueKind() == JsonValueKind.String
            ? o["receipt"]!.GetValue<string>()
            : null;

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
        var b = new MachineBuilder<CheckoutState, CheckoutTrigger>();
        b.Id("checkout").Version(1).StartsAt(CheckoutState.Cart, FreshCart);

        b.In(CheckoutState.Cart)
            .Holds(ctx =>
                ItemsIsArray(ctx) && TotalIsNumber(ctx) && ReceiptEmpty(ctx)
                    ? null
                    : "Cart: items[], a numeric total, and no receipt."
            )
            .On(CheckoutTrigger.Next)
            .When((ctx, _) => ItemsCount(ctx) > 0)
            .Because("Add an item before reviewing.")
            .To(CheckoutState.Review);

        b.In(CheckoutState.Review)
            .Holds(ctx =>
                ItemsCount(ctx) > 0 && Total(ctx) > 0 && ReceiptEmpty(ctx)
                    ? null
                    : "Review: non-empty items, a positive total, and no receipt."
            )
            .On(CheckoutTrigger.Back)
            .To(CheckoutState.Cart)
            .On(CheckoutTrigger.Pay)
            .When(
                (ctx, input) =>
                    ItemsCount(ctx) > 0 && Total(ctx) > 0 && ReceiptInput(input) is not null
            )
            .Because("A payable order needs items, a positive total, and a receipt.")
            .RunsOnce<ICheckoutCharge>("checkout:charge")
            .Reduce(
                (ctx, input) =>
                {
                    var next = (JsonObject)ctx.DeepClone();
                    next["receipt"] = ReceiptInput(input);
                    return next;
                }
            )
            .To(CheckoutState.Paid);

        b.In(CheckoutState.Paid)
            .Committed()
            .Holds(ctx =>
                ItemsCount(ctx) > 0 && ReceiptPresent(ctx)
                    ? null
                    : "Paid: non-empty items and a receipt."
            )
            .On(CheckoutTrigger.Restart)
            .Reduce((_, _) => FreshCart())
            .To(CheckoutState.Cart);

        return b.Build();
    }
}
