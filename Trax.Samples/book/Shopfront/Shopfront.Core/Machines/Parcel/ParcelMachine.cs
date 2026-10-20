using System.Text.Json.Nodes;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;

namespace Shopfront.Machines.Parcel;

public sealed class ParcelMachine : Machine<ParcelState, ParcelTrigger>
{
    protected override void Configure(IMachineBuilder<ParcelState, ParcelTrigger> m)
    {
        m.Id("parcel").Version(1).StartsAt(ParcelState.Packed, () => new JsonObject());

        m.In(ParcelState.Packed)
            .Context<ParcelContext>()
            .After(TimeSpan.FromMinutes(1))
            .GoTo(ParcelState.InTransit);
        m.In(ParcelState.InTransit)
            .Context<ParcelContext>()
            .After(TimeSpan.FromMinutes(1))
            .GoTo(ParcelState.Delivered);
        m.In(ParcelState.Delivered).Context<ParcelContext>().Ends();
    }
}
