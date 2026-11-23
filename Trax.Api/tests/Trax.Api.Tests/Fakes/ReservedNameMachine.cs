using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Api.Tests.Fakes.Machines;

/// <summary>
/// A machine with one event, whose name the subclass's trigger enum chooses: an event named like one of the
/// operations every machine has (<c>start</c>, <c>discard</c>, <c>get</c>) would collide with it.
/// </summary>
public abstract class ReservedNameMachine<TTrigger>(string id) : Machine<ReservedState, TTrigger>
    where TTrigger : struct, Enum
{
    protected override void Configure(IMachineBuilder<ReservedState, TTrigger> m)
    {
        m.Id(id).Version(1).StartsAt(ReservedState.Open, () => new JsonObject());
        m.Start<StepStartInput>(s => s.AnyAuthenticated());
        m.Get(r => r.OwnerOnly());
        m.Watch(r => r.OwnerOnly());
        m.Discard(r => r.OwnerOnly());
        m.In(ReservedState.Open)
            .Context()
            .On(Enum.GetValues<TTrigger>()[0])
            .AnyAuthenticated()
            .To(ReservedState.Closed);
    }
}
