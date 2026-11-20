using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.Samples.Recovery.Trains.Order;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

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
