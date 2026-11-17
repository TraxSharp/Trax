using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;

public enum FeatureState
{
    Idle,
    Deciding,
    Shortening,
    Done,
    Failed,
    Cancelled,
}
