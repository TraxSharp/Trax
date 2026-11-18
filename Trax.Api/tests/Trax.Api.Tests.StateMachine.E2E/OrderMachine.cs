using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.Core.Extensions;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.StateMachine.E2E;

/// <summary>
/// A neutral effectful wizard: <c>Draft -&gt; Review -&gt; Placed</c>, with <c>Placed</c> committed and the
/// irreversible <see cref="IOrderCharge"/> fired exactly once on <c>Place</c>, both declared inline.
/// </summary>
public sealed class OrderMachine : Machine<OrderState, OrderTrigger>
{
    private static int ItemsCount(JsonObject ctx) => ctx["items"] is JsonArray a ? a.Count : 0;

    private static bool ItemsIsArray(JsonObject ctx) => ctx["items"] is JsonArray;

    private static bool ReceiptEmpty(JsonObject ctx) =>
        ctx["receipt"] is null || ctx["receipt"].NotNull().GetValueKind() == JsonValueKind.Null;

    private static bool ReceiptPresent(JsonObject ctx) =>
        ctx["receipt"]?.GetValueKind() == JsonValueKind.String
        && ctx["receipt"].NotNull().GetValue<string>().Length > 0;

    private static string? Receipt(JsonNode? input) =>
        input is JsonObject o && o["receipt"]?.GetValueKind() == JsonValueKind.String
            ? o["receipt"].NotNull().GetValue<string>()
            : null;

    private static JsonObject Fresh() => new() { ["items"] = new JsonArray(), ["receipt"] = null };

    protected override void Configure(IMachineBuilder<OrderState, OrderTrigger> m)
    {
        m.Id("order")
            .Version(1)
            .StartsAt(OrderState.Draft, Fresh)
            .Start<EmptyStart>(s => s.Reduce(Rules.Keep()).AnyAuthenticated())
            .Get(r => r.OwnerOnly())
            .Watch(r => r.OwnerOnly())
            .Discard(r => r.OwnerOnly());

        m.In(OrderState.Draft)
            .Holds(ctx =>
                ItemsIsArray(ctx) && ReceiptEmpty(ctx) ? null : "Draft: items[] and no receipt."
            )
            .On(OrderTrigger.Next)
            .AnyAuthenticated()
            .To(OrderState.Review);

        m.In(OrderState.Review)
            .Holds(ctx =>
                ItemsCount(ctx) > 0 && ReceiptEmpty(ctx)
                    ? null
                    : "Review: non-empty items and no receipt."
            )
            .On(OrderTrigger.Back)
            .AnyAuthenticated()
            .To(OrderState.Draft)
            .On(OrderTrigger.Place)
            .AnyAuthenticated()
            .When((ctx, input) => ItemsCount(ctx) > 0 && Receipt(input) is not null)
            .Because("An order needs items and a receipt to be placed.")
            .RunsOnce<IOrderCharge>("order:place")
            .Reduce(
                (ctx, input) =>
                {
                    var next = (JsonObject)ctx.DeepClone();
                    next["receipt"] = Receipt(input);
                    return next;
                }
            )
            .To(OrderState.Placed);

        m.In(OrderState.Placed)
            .Committed()
            .Holds(ctx =>
                ItemsCount(ctx) > 0 && ReceiptPresent(ctx)
                    ? null
                    : "Placed: non-empty items and a receipt."
            )
            .On(OrderTrigger.Reset)
            .AnyAuthenticated()
            .Reduce(Rules.Reset())
            .To(OrderState.Draft);
    }

    /// <summary>A full Review-order snapshot (with items) as canonical JSON, for seeding via the save mutation.</summary>
    public static string ReviewSnapshot(params int[] items)
    {
        var array = new JsonArray();
        foreach (var item in items)
            array.Add(item);
        return new JsonObject
        {
            ["machine"] = "order",
            ["version"] = 1,
            ["state"] = "Review",
            ["context"] = new JsonObject { ["items"] = array, ["receipt"] = null },
        }.ToJsonString();
    }
}
