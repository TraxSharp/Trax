using System.Text.Json.Nodes;
using Trax.Effect.Data.Services.DataContext;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

public enum QueueingTimerState
{
    Idle,
    Waiting,
    Building,
    Built,
    Failed,
    Cancelled,
}
