using System.Text.Json.Nodes;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

public sealed class PlainJunctionMachine : StageMachine<IPlainJunctionTrain, PlainInput, JobOutput>
{
    protected override string Id => "plain-junction-stage";

    protected override PlainInput Input(string source) => new(source);
}
