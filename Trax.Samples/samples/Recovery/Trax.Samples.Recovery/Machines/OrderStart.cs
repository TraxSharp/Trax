using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.Samples.Recovery.Trains.Order;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Samples.Recovery.Machines;

/// <summary>What a user starts an order with: nothing, the cart starts empty.</summary>
public sealed record OrderStart;
