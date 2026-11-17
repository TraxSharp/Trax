using System.Text.Json.Nodes;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

/// <summary>A user's machine whose first stage's outcome enters the second stage, which invokes a train.</summary>
public sealed class UserChainMachine : ChainStageMachine
{
    public const string MachineId = "user-chain-stage";
    protected override string Id => MachineId;
}
