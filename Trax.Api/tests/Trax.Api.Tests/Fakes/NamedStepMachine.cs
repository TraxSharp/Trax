using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Api.Tests.Fakes.Machines;

/// <summary>
/// A minimal declarative machine whose id the subclass chooses, for the tests about what a machine's id becomes in
/// the schema.
/// </summary>
public abstract class NamedStepMachine(string id) : Machine<StepState, StepTrigger>
{
    protected override void Configure(IMachineBuilder<StepState, StepTrigger> m)
    {
        m.Id(id).Version(1).StartsAt(StepState.First, () => new JsonObject());
        m.Start<StepStartInput>(s => s.AnyAuthenticated());
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
