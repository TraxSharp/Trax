using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.Samples.Board.Auth;
using Trax.Samples.Board.Trains.Checks;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Samples.Board.Machines;

/// <summary>A patch to the shipping rules: the free-shipping threshold, and whether it is included.</summary>
public sealed record RulesPatch
{
    public decimal FreeFrom { get; init; }

    public bool Inclusive { get; init; }
}
