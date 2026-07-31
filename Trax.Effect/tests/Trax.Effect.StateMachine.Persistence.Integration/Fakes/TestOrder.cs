using System.Text.Json;
using System.Text.Json.Nodes;

namespace Trax.Effect.StateMachine.Persistence.Integration.Fakes;

public enum OrderState
{
    Draft,
    Review,
    Placed,
}

public enum OrderTrigger
{
    Next,
    Back,
    Place,
    Reset,
}

/// <summary>
/// A neutral effectful wizard: <c>Draft -&gt; Review -&gt; Placed</c>, with <c>Placed</c> committed and an
/// irreversible "place the order" effect on the <c>Place</c> transition. It exercises everything the
/// turnstile can't: a multi-key context, a committed state (the guarded autosave path), and an
/// exactly-once effect. Context is <c>{ items: string[], receipt: string | null }</c>.
/// </summary>
public static class TestOrder
{
    public const string Id = "order";

    private static bool ItemsIsArray(JsonObject ctx) => ctx["items"] is JsonArray;

    private static bool ItemsNonEmpty(JsonObject ctx) => ctx["items"] is JsonArray a && a.Count > 0;

    private static bool ReceiptEmpty(JsonObject ctx) =>
        ctx["receipt"] is null || ctx["receipt"]!.GetValueKind() == JsonValueKind.Null;

    private static bool ReceiptPresent(JsonObject ctx) =>
        ctx["receipt"]?.GetValueKind() == JsonValueKind.String
        && ctx["receipt"]!.GetValue<string>().Length > 0;

    private static string? OrderId(JsonNode? input) =>
        input is JsonObject o && o["orderId"]?.GetValueKind() == JsonValueKind.String
            ? o["orderId"]!.GetValue<string>()
            : null;

    public static readonly MachineDefinition<OrderState, OrderTrigger> Definition = new()
    {
        Id = Id,
        Version = 1,
        InitialState = OrderState.Draft,
        CreateInitialContext = () =>
            new JsonObject { ["items"] = new JsonArray(), ["receipt"] = null },
        Transitions = new[]
        {
            new TransitionDefinition<OrderState, OrderTrigger>
            {
                From = OrderState.Draft,
                Trigger = OrderTrigger.Next,
                To = OrderState.Review,
            },
            new TransitionDefinition<OrderState, OrderTrigger>
            {
                From = OrderState.Review,
                Trigger = OrderTrigger.Back,
                To = OrderState.Draft,
            },
            new TransitionDefinition<OrderState, OrderTrigger>
            {
                From = OrderState.Review,
                Trigger = OrderTrigger.Place,
                To = OrderState.Placed,
                Guard = (ctx, input) => ItemsNonEmpty(ctx) && !string.IsNullOrEmpty(OrderId(input)),
                GuardMessage = "An order needs items and a receipt to be placed.",
                Reduce = (ctx, input) =>
                {
                    var next = (JsonObject)ctx.DeepClone();
                    next["receipt"] = OrderId(input);
                    return next;
                },
            },
            new TransitionDefinition<OrderState, OrderTrigger>
            {
                From = OrderState.Placed,
                Trigger = OrderTrigger.Reset,
                To = OrderState.Draft,
                Reduce = (_, _) =>
                    new JsonObject { ["items"] = new JsonArray(), ["receipt"] = null },
            },
        },
        ContextValidators = new Dictionary<OrderState, Func<JsonObject, string?>>
        {
            [OrderState.Draft] = ctx =>
                ItemsIsArray(ctx) && ReceiptEmpty(ctx) ? null : "Draft: items[] and no receipt.",
            [OrderState.Review] = ctx =>
                ItemsNonEmpty(ctx) && ReceiptEmpty(ctx)
                    ? null
                    : "Review: non-empty items[] and no receipt.",
            [OrderState.Placed] = ctx =>
                ItemsNonEmpty(ctx) && ReceiptPresent(ctx)
                    ? null
                    : "Placed: non-empty items[] and a receipt.",
        },
    };

    public static readonly SnapshotMachine<OrderState, OrderTrigger> Machine = new(Definition);

    public static string EffectKey(string userKey, Guid id) => $"order:place:{userKey}:{id}";

    /// <summary>A draft service wired WITH the committed state and the effect-claim release hook.</summary>
    public static SnapshotDraftService<OrderState, OrderTrigger> Service(
        ISnapshotStore store,
        IEffectClaimStore? claims = null
    ) =>
        new(
            Machine,
            store,
            committedStates: new[] { OrderState.Placed },
            effectClaims: claims,
            effectKeysOnReset: (userKey, id) => new[] { EffectKey(userKey, id) }
        );

    /// <summary>A Review snapshot with the given items (a complete, placeable order).</summary>
    public static string ReviewJson(params int[] items)
    {
        var arr = new JsonArray();
        foreach (var i in items)
            arr.Add(i);
        return Machine.Serialize(
            new Snapshot
            {
                Machine = Id,
                Version = 1,
                State = "Review",
                Context = new JsonObject { ["items"] = arr, ["receipt"] = null },
            }
        );
    }

    public static string DraftJson => Machine.Serialize(Machine.Definition.CreateInitialSnapshot());
}
