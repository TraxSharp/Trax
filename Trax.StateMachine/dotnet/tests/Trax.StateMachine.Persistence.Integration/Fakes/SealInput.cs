using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

public sealed record SealInput
{
    [MinLength(1)]
    public string Label { get; init; } = "";
}
