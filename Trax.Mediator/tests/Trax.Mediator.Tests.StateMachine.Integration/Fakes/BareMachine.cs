using System.Text.Json.Nodes;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

public sealed class BareMachine : StageMachine<IBareTrain, BareInput, JobOutput>
{
    protected override string Id => "bare-stage";

    protected override BareInput Input(string source) => new(source);
}
