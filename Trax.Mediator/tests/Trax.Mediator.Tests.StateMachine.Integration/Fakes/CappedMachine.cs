using System.Text.Json.Nodes;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

/// <summary>A user's machine with a live-run cap of 2.</summary>
public sealed class CappedMachine : StageMachine<IGoodTrain, GoodInput, JobOutput>
{
    public const string MachineId = "capped-stage";
    protected override string Id => MachineId;
    protected override int? Limit => 2;

    protected override GoodInput Input(string source) => new(source);
}
