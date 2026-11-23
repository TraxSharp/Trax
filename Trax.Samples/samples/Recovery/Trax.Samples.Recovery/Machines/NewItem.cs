using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.Samples.Recovery.Trains.Order;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Samples.Recovery.Machines;

/// <summary>The input of <see cref="OrderTrigger.AddItem"/>: an item on the menu. The server prices it.</summary>
public sealed record NewItem
{
    public string Item { get; init; } = "";
}
