using System.Text.Json.Nodes;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

public sealed class ForkMachine : StageMachine<IForkTrain, ForkInput, JobOutput>
{
    protected override string Id => "fork-stage";

    protected override ForkInput Input(string source) => new(source);
}
