using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Api.Tests.Fakes.Machines;

/// <summary>Declares its reads and start, but its <c>Back</c> event names no rule.</summary>
[TraxMachine]
public sealed class UndeclaredRuleMachine : Machine<StepState, StepTrigger>
{
    public const string MachineId = "undeclared-rule";

    protected override void Configure(IMachineBuilder<StepState, StepTrigger> m)
    {
        m.Id(MachineId).Version(1).StartsAt(StepState.First, () => new JsonObject());
        m.Start<StepStartInput>(s => s.AnyAuthenticated());
        m.Get(r => r.OwnerOnly());
        m.Watch(r => r.OwnerOnly());
        m.Discard(r => r.OwnerOnly());
        m.In(StepState.First)
            .Context()
            .On(StepTrigger.Next)
            .AnyAuthenticated()
            .To(StepState.Second);
        m.In(StepState.Second).Context().On(StepTrigger.Back).To(StepState.First);
    }
}
