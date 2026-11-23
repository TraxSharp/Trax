using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Api.Tests.Fakes.Machines;

/// <summary>A machine whose context holds a <typeparamref name="TValue"/>, under the id the subclass gives.</summary>
public abstract class HoldingMachine<TValue>(string id) : Machine<HeldState, HeldTrigger>
{
    protected override void Configure(IMachineBuilder<HeldState, HeldTrigger> m)
    {
        m.Id(id).Version(1).StartsAt(HeldState.Open, () => new JsonObject());
        m.Start<StepStartInput>(s => s.AnyAuthenticated());
        m.Get(r => r.OwnerOnly());
        m.Watch(r => r.OwnerOnly());
        m.Discard(r => r.OwnerOnly());
        m.In(HeldState.Open)
            .Context<HeldContext<TValue>>()
            .On(HeldTrigger.Close)
            .AnyAuthenticated()
            .To(HeldState.Done);
        m.In(HeldState.Done).Context<HeldContext<TValue>>();
    }
}
