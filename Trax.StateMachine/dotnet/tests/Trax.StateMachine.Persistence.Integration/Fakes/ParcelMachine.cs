using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

/// <summary>The child a consignment spawns: sealed, it ends; dropped, it fails.</summary>
public sealed class ParcelMachine : Machine<ParcelState, ParcelTrigger>
{
    public const string Id = "parcel";

    public sealed record ParcelContext
    {
        public string Label { get; init; } = "";

        public Guid? InspectionId { get; init; }
    }

    protected override void Configure(IMachineBuilder<ParcelState, ParcelTrigger> m)
    {
        m.Id(Id)
            .Version(1)
            .StartsAt(
                ParcelState.Packing,
                () => new JsonObject { ["label"] = "", ["inspectionId"] = null }
            );

        m.In(ParcelState.Packing)
            .Context<ParcelContext>()
            .On(ParcelTrigger.Seal)
            .WithInput<SealInput>()
            .Reduce(Set((ParcelContext c) => c.Label).FromInput((SealInput i) => i.Label))
            .To(ParcelState.Packed)
            .On(ParcelTrigger.Drop)
            .To(ParcelState.Damaged)
            .On(ParcelTrigger.Inspect)
            .To(ParcelState.Inspecting);

        // A parcel can wait on a child of its own, which leaving the consignment's state removes with it.
        m.In(ParcelState.Inspecting)
            .Context<ParcelContext>()
            .Spawns<InspectionMachine, ParcelContext, InspectionResult>(c => c.InspectionId)
            .OnDone(ParcelState.Checked)
            .OnFailed(ParcelState.CheckFailed)
            .OnCancelled(ParcelState.CheckCancelled);

        m.In(ParcelState.Packed).Context<ParcelContext>().Ends();
        m.In(ParcelState.Damaged).Context<ParcelContext>().Fails();
        m.In(ParcelState.Checked).Context<ParcelContext>();
        m.In(ParcelState.CheckFailed).Context<ParcelContext>();
        m.In(ParcelState.CheckCancelled).Context<ParcelContext>();
    }
}
