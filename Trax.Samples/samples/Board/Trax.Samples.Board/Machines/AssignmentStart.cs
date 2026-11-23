using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.Samples.Board.Auth;
using Trax.Samples.Board.Trains.Checks;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Samples.Board.Machines;

/// <summary>What an agent starts an assignment with: the ticket its claim gave it.</summary>
public sealed record AssignmentStart
{
    [MinLength(1)]
    public string TicketId { get; init; } = "";
}
