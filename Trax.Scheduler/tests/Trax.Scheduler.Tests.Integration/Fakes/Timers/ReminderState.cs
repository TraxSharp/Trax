using System.Text.Json.Nodes;
using Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Scheduler.Tests.Integration.Fakes.Timers;

public enum ReminderState
{
    Idle,
    Review,
    Expired,
    Done,
    Published,
}
