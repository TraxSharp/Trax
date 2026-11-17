using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

public sealed record PendingPayContext
{
    public string Note { get; init; } = "";
    public string Receipt { get; init; } = "";
}
