using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.Samples.Board.Auth;
using Trax.Samples.Board.Trains.Checks;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Samples.Board.Machines;

/// <summary>A case that should show the bug: an order's subtotal and the shipping the policy says it pays.</summary>
public sealed record ReproCase
{
    public decimal Subtotal { get; init; }

    public decimal ExpectedShipping { get; init; }
}
