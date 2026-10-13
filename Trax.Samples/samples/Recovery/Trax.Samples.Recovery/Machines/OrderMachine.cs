using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;
using Trax.Samples.Recovery.Trains.Order;
using static Trax.Effect.StateMachine.Rules;

namespace Trax.Samples.Recovery.Machines;

public enum OrderState
{
    /// <summary>Choosing what to eat.</summary>
    Cart,

    /// <summary>The order is on its way: <see cref="IDeliverOrderTrain"/> runs.</summary>
    Ordered,

    /// <summary>The run finished: the food is at the door.</summary>
    Delivered,

    /// <summary>The run failed or was cancelled, before the food arrived.</summary>
    Cancelled,

    /// <summary>Nobody ordered within <see cref="OrderMachine.CartWait"/>: the server cleared the cart.</summary>
    Cleared,
}

public enum OrderTrigger
{
    /// <summary>Put an item from the menu in the cart.</summary>
    AddItem,

    /// <summary>Order what is in the cart.</summary>
    PlaceOrder,
}

/// <summary>The order, one shape in every state: what is in the cart, and what it costs.</summary>
public sealed record OrderContext
{
    public List<string>? Items { get; init; }

    public decimal? Total { get; init; }
}

/// <summary>What a user starts an order with: nothing, the cart starts empty.</summary>
public sealed record OrderStart;

/// <summary>The input of <see cref="OrderTrigger.AddItem"/>: an item on the menu. The server prices it.</summary>
public sealed record NewItem
{
    public string Item { get; init; } = "";
}

/// <summary>
/// "Order a pizza": fill the cart, place the order, and the server takes it from there. <c>Ordered</c> runs
/// <see cref="IDeliverOrderTrain"/>, and only that run's outcome moves the order on.
/// <code>
/// Cart --PlaceOrder--> Ordered --done-----------------> Delivered
///   │                          --failed or cancelled--> Cancelled
///   └──after 30 minutes──> Cleared
/// </code>
/// </summary>
/// <remarks>
/// User-owned: the order is the user's own, and their laptop and their phone both follow it by
/// <c>machineChanged</c>. A client sends only its events; no client fires the run's outcome or the cart's timer.
/// </remarks>
[TraxMachine]
public sealed class OrderMachine : Machine<OrderState, OrderTrigger>
{
    public const string MachineId = "order";

    /// <summary>What the kitchen makes, and its price.</summary>
    public static readonly Dictionary<string, decimal> Menu = new()
    {
        ["Margherita"] = 12m,
        ["Pepperoni"] = 14m,
        ["Garlic bread"] = 5m,
    };

    /// <summary>How long a cart waits for its order: 30 minutes. The demo host shortens it (Recovery:CartWait).</summary>
    public static TimeSpan CartWait { get; set; } = TimeSpan.FromMinutes(30);

    protected override void Configure(IMachineBuilder<OrderState, OrderTrigger> m)
    {
        m.Id(MachineId)
            .Version(1)
            .StartsAt(OrderState.Cart, () => new JsonObject())
            .Start<OrderStart>(s => s.Reduce(Keep()).AnyAuthenticated())
            .Get(r => r.OwnerOnly())
            .Watch(r => r.OwnerOnly())
            .Discard(r => r.OwnerOnly())
            .CustomReducer("order:add-item", AddToCart);

        m.In(OrderState.Cart)
            .Context<OrderContext>()
            // Nobody ordered: the server clears the cart by itself, and every screen hears it by push.
            .After(CartWait) // 30 minutes; the demo host shortens it
            .GoTo(OrderState.Cleared)
            .On(OrderTrigger.AddItem)
            .AnyAuthenticated()
            .WithInput<NewItem>()
            .When(Input((NewItem i) => i.Item).IsOneOf([.. Menu.Keys]))
            .Because("That is not on the menu.")
            .Reduce(new Reduction.Custom("order:add-item"))
            .To(OrderState.Cart)
            .On(OrderTrigger.PlaceOrder)
            .AnyAuthenticated()
            .When(Field((OrderContext c) => c.Items).CountAtLeast(1))
            .Because("The cart is empty.")
            .To(OrderState.Ordered);

        m.In(OrderState.Ordered)
            .Context<OrderContext>()
            .Invokes<IDeliverOrderTrain, OrderInput, OrderDelivered>(DeliveryFor)
            .OnDone(OrderState.Delivered)
            .OnFailed(OrderState.Cancelled)
            .OnCancelled(OrderState.Cancelled);

        m.In(OrderState.Delivered).Context<OrderContext>();
        m.In(OrderState.Cancelled).Context<OrderContext>();
        m.In(OrderState.Cleared).Context<OrderContext>();

        m.Differential(d =>
            d.Sample(OrderTrigger.AddItem, new NewItem { Item = "Margherita" })
                .Sample(OrderTrigger.AddItem, new NewItem { Item = "Sushi" })
                .EmptySample(OrderTrigger.AddItem)
                .OutcomeSample(OrderState.Ordered, new OrderDelivered("order-sample", "Sam"))
        );
    }

    // The run's input, built on the server from the cart the order was placed with. Each order gets an id of its
    // own, which the card is charged under.
    private static OrderInput DeliveryFor(JsonObject cart) =>
        new()
        {
            OrderId = $"order-{Guid.NewGuid():N}",
            Items = cart["items"]!.AsArray().Select(i => i!.GetValue<string>()).ToList(),
            Total = cart["total"]!.GetValue<decimal>(),
        };

    // AddItem's reduction: the item goes in the cart at the menu's price, never one a client sends.
    private static JsonObject AddToCart(JsonObject cart, JsonNode? input)
    {
        var item = input!["item"]!.GetValue<string>();
        var items = cart["items"]?.AsArray().Select(i => i!.GetValue<string>()).ToList() ?? [];
        var total = cart["total"]?.GetValue<decimal>() ?? 0m;
        return new JsonObject
        {
            ["items"] = new JsonArray([.. items.Append(item).Select(i => JsonValue.Create(i))]),
            ["total"] = total + Menu[item],
        };
    }
}
