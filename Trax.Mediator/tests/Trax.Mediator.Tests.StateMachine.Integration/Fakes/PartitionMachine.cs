using System.Text.Json.Nodes;
using Trax.Effect.Services.ServiceTrain;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Mediator.Tests.StateMachine.Integration.Fakes;

/// <summary>A system-owned machine whose initial state invokes a train it may.</summary>
public sealed class PartitionMachine : StageMachine<IGoodTrain, GoodInput, JobOutput>
{
    public const string MachineId = "partition-stage";
    protected override string Id => MachineId;
    protected override bool System => true;

    protected override GoodInput Input(string source) => new(source);
}
