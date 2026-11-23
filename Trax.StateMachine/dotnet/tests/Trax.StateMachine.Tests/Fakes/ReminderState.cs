using System.Text.Json.Nodes;

namespace Trax.StateMachine.Tests.Fakes;

public enum ReminderState
{
    Idle,
    Review,
    Expired,
    Done,
}
