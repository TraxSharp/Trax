using System.Text.Json.Nodes;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

public enum ChainState
{
    Idle,
    Fetching,
    Fetched,
    Embedding,
    Done,
    Failed,
    Cancelled,
}
