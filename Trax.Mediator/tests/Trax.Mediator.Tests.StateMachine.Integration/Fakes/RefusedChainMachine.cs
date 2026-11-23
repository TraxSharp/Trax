using System.Text.Json.Nodes;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

public sealed class RefusedChainMachine : StageMachine<IRefusedChainTrain, RefusedInput, JobOutput>
{
    protected override string Id => "refused-chain-stage";

    protected override RefusedInput Input(string source) => new(source);
}
