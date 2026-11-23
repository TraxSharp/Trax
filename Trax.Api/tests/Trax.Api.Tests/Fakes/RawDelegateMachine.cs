using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Api.Tests.Fakes.Machines;

/// <summary>Raw delegates throughout: no IR, no input schema, nothing a typed surface can be built from.</summary>
[TraxMachine]
public sealed class RawDelegateMachine : Machine<StepState, StepTrigger>
{
    public const string MachineId = "raw-delegate";

    protected override void Configure(IMachineBuilder<StepState, StepTrigger> m)
    {
        m.Id(MachineId).Version(1).StartsAt(StepState.First, () => new JsonObject());
        m.In(StepState.First)
            .Holds(_ => null)
            .On(StepTrigger.Next)
            .When((_, input) => input is JsonObject)
            .Reduce((ctx, _) => ctx)
            .To(StepState.Second);
        m.In(StepState.Second).Holds(_ => null).On(StepTrigger.Back).To(StepState.First);
    }
}
