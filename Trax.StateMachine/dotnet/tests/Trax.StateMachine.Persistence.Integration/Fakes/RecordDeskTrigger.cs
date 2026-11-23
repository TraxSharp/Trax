using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

public enum RecordDeskTrigger
{
    Submit,
    Revise,
    Reject,
    Note,
    Resume,
}
