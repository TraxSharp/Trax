using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Samples.Board.Machines;

public sealed record ClaimInput
{
    [MinLength(1)]
    public string By { get; init; } = "";

    [MinLength(1)]
    public string At { get; init; } = "";
}
