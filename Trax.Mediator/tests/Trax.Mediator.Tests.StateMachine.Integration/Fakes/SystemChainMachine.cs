using System.Text.Json.Nodes;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

/// <summary>The same chain, system-owned.</summary>
public sealed class SystemChainMachine : ChainStageMachine
{
    public const string MachineId = "system-chain-stage";
    protected override string Id => MachineId;
    protected override bool System => true;
}
