using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.Samples.Recovery.Trains.Order;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Samples.Recovery.Machines;

/// <summary>The order, one shape in every state: what is in the cart, and what it costs.</summary>
public sealed record OrderContext
{
    public List<string>? Items { get; init; }

    public decimal? Total { get; init; }
}
