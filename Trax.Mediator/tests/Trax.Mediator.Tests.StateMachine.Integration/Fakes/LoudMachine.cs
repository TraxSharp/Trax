using System.Text.Json.Nodes;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

public sealed class LoudMachine : StageMachine<ILoudTrain, LoudInput, JobOutput>
{
    protected override string Id => "loud-stage";

    protected override LoudInput Input(string source) => new(source);
}
