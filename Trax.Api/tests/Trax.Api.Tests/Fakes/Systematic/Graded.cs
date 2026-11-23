using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Systematic.Orders;

public sealed record Graded
{
    public Grade Grade { get; init; }
}
