using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Api.Tests.Fakes.Machines;

/// <summary>Declares every event's rule and its start, but no rule for <c>get</c>.</summary>
[TraxMachine]
public sealed class NoReadRuleMachine : Machine<StepState, StepTrigger>
{
    public const string MachineId = "no-read-rule";

    protected override void Configure(IMachineBuilder<StepState, StepTrigger> m)
    {
        m.Id(MachineId).Version(1).StartsAt(StepState.First, () => new JsonObject());
        m.Start<StepStartInput>(s => s.AnyAuthenticated());
        m.Watch(r => r.OwnerOnly());
        m.Discard(r => r.OwnerOnly());
        m.In(StepState.First)
            .Context()
            .On(StepTrigger.Next)
            .AnyAuthenticated()
            .To(StepState.Second);
        m.In(StepState.Second)
            .Context()
            .On(StepTrigger.Back)
            .AnyAuthenticated()
            .To(StepState.First);
    }
}
