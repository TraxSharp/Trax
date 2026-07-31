using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Effect.StateMachine.Tests.Stress.Fakes;

public enum OrderState
{
    Draft,
    Review,
    Placed,
}

public enum OrderTrigger
{
    Next,
    Place,
    Reset,
}

/// <summary>
/// An effectful order machine (<c>Draft → Review → Placed</c>), the same shape the correctness suite uses:
/// <c>Placed</c> is committed and <c>Place</c> runs the irreversible effect exactly once. The stress tests
/// drive this at volume and under contention.
/// </summary>
public static class StressOrder
{
    private static bool ItemsNonEmpty(JsonObject ctx) => ctx["items"] is JsonArray { Count: > 0 };

    private static bool ItemsIsArray(JsonObject ctx) => ctx["items"] is JsonArray;

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
        Id = "order",
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

    public static string ReviewJson(params int[] items)
    {
        var arr = new JsonArray();
        foreach (var i in items)
            arr.Add(i);
        return Machine.Serialize(
            new Snapshot
            {
                Machine = "order",
                Version = 1,
                State = "Review",
                Context = new JsonObject { ["items"] = arr, ["receipt"] = null },
            }
        );
    }

    public static string DraftJson => Machine.Serialize(Machine.Definition.CreateInitialSnapshot());

    public static string DraftWithItems(params int[] items)
    {
        var arr = new JsonArray();
        foreach (var i in items)
            arr.Add(i);
        return Machine.Serialize(
            new Snapshot
            {
                Machine = "order",
                Version = 1,
                State = "Draft",
                Context = new JsonObject { ["items"] = arr, ["receipt"] = null },
            }
        );
    }
}
