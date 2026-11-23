using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Samples.Board.Machines;

public sealed record TriagedInput
{
    [MinLength(1)]
    public string By { get; init; } = "";

    [MinLength(1)]
    public string Note { get; init; } = "";

    [MinLength(1)]
    public string Area { get; init; } = "";
}
