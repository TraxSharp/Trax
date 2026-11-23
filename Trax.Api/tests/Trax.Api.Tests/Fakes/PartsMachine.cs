using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;
using static Trax.StateMachine.Rules;

namespace Trax.Api.Tests.Fakes.Machines;

/// <summary>A second machine sharing <see cref="ShipToInput"/> with <see cref="ShopMachine"/>.</summary>
[TraxMachine]
public sealed class PartsMachine : Machine<PartsState, PartsTrigger>
{
    public const string MachineId = "parts";

    protected override void Configure(IMachineBuilder<PartsState, PartsTrigger> m)
    {
        m.Id(MachineId).Version(1).StartsAt(PartsState.Picking, () => new JsonObject());
        m.Start<PartsStartInput>(s =>
            s.Reduce(Set((PartsContext c) => c.Sku).FromInput((PartsStartInput i) => i.Sku))
                .AnyAuthenticated()
        );
        m.Get(r => r.OwnerOnly());
        m.Watch(r => r.OwnerOnly());
        m.Discard(r => r.OwnerOnly());

        m.In(PartsState.Picking)
            .Context<PartsContext>()
            .On(PartsTrigger.Lookup)
            .WithInput<CatalogLookupInput>()
            .AnyAuthenticated()
            .Reduce(Set((PartsContext c) => c.Sku).FromInput((CatalogLookupInput i) => i.Sku))
            .To(PartsState.Picking)
            .On(PartsTrigger.Ship)
            .WithInput<ShipToInput>()
            .AnyAuthenticated()
            .Reduce(Set((PartsContext c) => c.Street).FromInput((ShipToInput i) => i.Street))
            .To(PartsState.Shipped);
    }
}
