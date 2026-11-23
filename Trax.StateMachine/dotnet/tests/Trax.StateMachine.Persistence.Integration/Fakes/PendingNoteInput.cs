using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

public sealed record PendingNoteInput
{
    public string Note { get; init; } = "";
}
