using System.Text.Json.Nodes;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

public sealed class ContractMachine : StageMachine<IContractTrain, ContractInput, JobOutput>
{
    protected override string Id => "contract-stage";

    protected override ContractInput Input(string source) => new(source);
}
