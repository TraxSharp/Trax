using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.Core.Extensions;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>
/// The order machine authored as a discoverable <see cref="Machine{TState,TTrigger}"/>: <c>Placed</c> is
/// committed and <c>Pay</c> runs the irreversible <see cref="IOrderCharge"/> exactly once, both declared
/// inline. A host reads that off the build and wires it, no per-machine registration.
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

    protected override void Configure(IMachineBuilder<OrderState, OrderTrigger> m) =>
        Declare(m, "order");

    /// <summary>The order's states, edges and effect under <paramref name="id"/>, shared with its system-owned twin.</summary>
    internal static void Declare(IMachineBuilder<OrderState, OrderTrigger> m, string id)
    {
        m.Id(id).Version(1).StartsAt(OrderState.Draft, Fresh);

        m.In(OrderState.Draft)
            .Holds(ctx =>
                ItemsIsArray(ctx) && ReceiptEmpty(ctx) ? null : "Draft: items[] and no receipt."
            )
            .On(OrderTrigger.Next)
            .To(OrderState.Review);

        m.In(OrderState.Review)
            .Holds(ctx =>
                ItemsCount(ctx) > 0 && ReceiptEmpty(ctx)
                    ? null
                    : "Review: non-empty items and no receipt."
            )
            .On(OrderTrigger.Back)
            .To(OrderState.Draft)
            .On(OrderTrigger.Place)
            .When((ctx, input) => ItemsCount(ctx) > 0 && Receipt(input) is not null)
            .Because("An order needs items and a receipt to be placed.")
            .RunsOnce<IOrderCharge>($"{id}:place")
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
            .Reduce((_, _) => Fresh())
            .To(OrderState.Draft);
    }

    /// <summary>A full Review-order snapshot (with items) as JSON, for seeding via the save mutation.</summary>
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
