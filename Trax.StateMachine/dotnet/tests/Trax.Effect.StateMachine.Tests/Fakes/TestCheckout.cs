using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trax.Effect.StateMachine.Tests.Fakes;

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

/// <summary>
/// The C# twin of the TypeScript <c>checkout</c> machine — a 3-state, guard-branched flow over a
/// multi-key context. Behavior mirrors the TS definition exactly so both engines agree on the shared
/// checkout fixtures (including multi-key canonicalization, the case turnstile cannot exercise).
/// </summary>
public static class TestCheckout
{
    public const string Id = "checkout";

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

    public static readonly MachineDefinition<CheckoutState, CheckoutTrigger> Definition = new()
    {
        Id = Id,
        Version = 1,
        InitialState = CheckoutState.Cart,
        CreateInitialContext = () =>
            new JsonObject
            {
                ["currency"] = "USD",
                ["items"] = new JsonArray(),
                ["receipt"] = null,
                ["total"] = 0,
            },
        Transitions = new[]
        {
            new TransitionDefinition<CheckoutState, CheckoutTrigger>
            {
                From = CheckoutState.Cart,
                Trigger = CheckoutTrigger.Next,
                To = CheckoutState.Review,
                Guard = (ctx, _) => ItemsCount(ctx) > 0,
                GuardMessage = "Add an item before reviewing.",
            },
            new TransitionDefinition<CheckoutState, CheckoutTrigger>
            {
                From = CheckoutState.Review,
                Trigger = CheckoutTrigger.Back,
                To = CheckoutState.Cart,
            },
            new TransitionDefinition<CheckoutState, CheckoutTrigger>
            {
                From = CheckoutState.Review,
                Trigger = CheckoutTrigger.Pay,
                To = CheckoutState.Paid,
                Guard = (ctx, input) =>
                    ItemsCount(ctx) > 0 && Total(ctx) > 0 && ReceiptInput(input) is not null,
                GuardMessage = "A payable order needs items, a positive total, and a receipt.",
                Reduce = (ctx, input) =>
                {
                    var next = (JsonObject)ctx.DeepClone();
                    next["receipt"] = ReceiptInput(input);
                    return next;
                },
            },
            new TransitionDefinition<CheckoutState, CheckoutTrigger>
            {
                From = CheckoutState.Paid,
                Trigger = CheckoutTrigger.Restart,
                To = CheckoutState.Cart,
                Reduce = (_, _) =>
                    new JsonObject
                    {
                        ["currency"] = "USD",
                        ["items"] = new JsonArray(),
                        ["receipt"] = null,
                        ["total"] = 0,
                    },
            },
        },
        ContextValidators = new Dictionary<CheckoutState, Func<JsonObject, string?>>
        {
            [CheckoutState.Cart] = ctx =>
                ItemsIsArray(ctx) && TotalIsNumber(ctx) && ReceiptEmpty(ctx)
                    ? null
                    : "Cart: items[], a numeric total, and no receipt.",
            [CheckoutState.Review] = ctx =>
                ItemsCount(ctx) > 0 && Total(ctx) > 0 && ReceiptEmpty(ctx)
                    ? null
                    : "Review: non-empty items, a positive total, and no receipt.",
            [CheckoutState.Paid] = ctx =>
                ItemsCount(ctx) > 0 && ReceiptPresent(ctx)
                    ? null
                    : "Paid: non-empty items and a receipt.",
        },
    };

    public static readonly SnapshotMachine<CheckoutState, CheckoutTrigger> Machine = new(
        Definition
    );
}
