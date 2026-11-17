using System.Text.Json.Nodes;
using Trax.Effect.Services.ServiceTrain;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

public enum ExpiringTrigger
{
    Close,
}
