using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

public enum RecordDeskState
{
    Draft,
    Review,
    Revising,
    Archived,
}
