using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>The system's parcel: sealed, it ends.</summary>
public sealed class SystemParcelMachine : Machine<ParcelState, ParcelTrigger>
{
    public const string Id = "system-parcel";

    protected override void Configure(IMachineBuilder<ParcelState, ParcelTrigger> m)
    {
        m.Id(Id)
            .Version(1)
            .SystemOwned()
            .StartsAt(
                ParcelState.Packing,
                () => new JsonObject { ["label"] = "", ["inspectionId"] = null }
            );

        m.In(ParcelState.Packing)
            .Context<ParcelMachine.ParcelContext>()
            .On(ParcelTrigger.Seal)
            .WithInput<SealInput>()
            .Reduce(
                Set((ParcelMachine.ParcelContext c) => c.Label).FromInput((SealInput i) => i.Label)
            )
            .To(ParcelState.Packed);

        m.In(ParcelState.Packed).Context<ParcelMachine.ParcelContext>().Ends();
    }
}
