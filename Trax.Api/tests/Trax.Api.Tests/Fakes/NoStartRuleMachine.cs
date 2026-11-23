using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Api.Tests.Fakes.Machines;

/// <summary>Declares its start without a rule for who may start one.</summary>
[TraxMachine]
public sealed class NoStartRuleMachine : Machine<StepState, StepTrigger>
{
    public const string MachineId = "no-start-rule";

    protected override void Configure(IMachineBuilder<StepState, StepTrigger> m)
    {
        m.Id(MachineId).Version(1).StartsAt(StepState.First, () => new JsonObject());
        m.Start<StepStartInput>(_ => { });
        m.Get(r => r.OwnerOnly());
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
