using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

public sealed record PendingReceiptInput
{
    public string Receipt { get; init; } = "";
}
