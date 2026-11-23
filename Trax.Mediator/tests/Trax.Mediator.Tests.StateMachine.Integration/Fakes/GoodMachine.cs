using System.Text.Json.Nodes;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

/// <summary>A user's machine invoking a train it may.</summary>
public sealed class GoodMachine : StageMachine<IGoodTrain, GoodInput, JobOutput>
{
    public const string MachineId = "good-stage";
    protected override string Id => MachineId;

    protected override GoodInput Input(string source) => new(source);
}
