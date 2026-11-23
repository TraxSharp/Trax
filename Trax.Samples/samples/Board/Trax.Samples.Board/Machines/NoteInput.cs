using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Samples.Board.Machines;

/// <summary>What a lane leaves on the ticket when it moves it: who, and a one-line note.</summary>
public sealed record NoteInput
{
    [MinLength(1)]
    public string By { get; init; } = "";

    [MinLength(1)]
    public string Note { get; init; } = "";
}
