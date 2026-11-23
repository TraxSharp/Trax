using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

public enum PendingPayTrigger
{
    Go,
    Pay,
    Cancel,
    Edit,
    Restart,
}
