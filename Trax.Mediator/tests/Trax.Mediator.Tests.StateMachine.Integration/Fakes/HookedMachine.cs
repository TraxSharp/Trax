using System.Text.Json.Nodes;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

public sealed class HookedMachine : StageMachine<IHookedTrain, HookedInput, JobOutput>
{
    protected override string Id => "hooked-stage";

    protected override HookedInput Input(string source) => new(source);
}
