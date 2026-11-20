using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.Samples.Board.Auth;
using Trax.Samples.Board.Trains.Checks;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Samples.Board.Machines;

/// <summary>Subtotals QA wants the patched rules run on.</summary>
public sealed record QaCases
{
    public IReadOnlyList<decimal> Subtotals { get; init; } = [];
}
