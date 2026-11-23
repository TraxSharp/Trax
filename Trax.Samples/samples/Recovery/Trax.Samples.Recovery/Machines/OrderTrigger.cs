using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.Samples.Recovery.Trains.Order;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Samples.Recovery.Machines;

public enum OrderTrigger
{
    /// <summary>Put an item from the menu in the cart.</summary>
    AddItem,

    /// <summary>Order what is in the cart.</summary>
    PlaceOrder,
}
