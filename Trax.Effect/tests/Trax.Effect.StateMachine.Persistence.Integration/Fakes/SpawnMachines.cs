using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using static Trax.Effect.StateMachine.Rules;

namespace Trax.Effect.StateMachine.Persistence.Integration.Fakes;

public enum ConsignmentState
{
    Open,
    Packing,
    Sent,
    Lost,
    Recalled,
    Abandoned,
}

public enum ConsignmentTrigger
{
    Pack,
    Abandon,
}

public enum ParcelState
{
    Packing,
    Packed,
    Damaged,
    Inspecting,
    Checked,
    CheckFailed,
    CheckCancelled,
}

public enum ParcelTrigger
{
    Seal,
    Drop,
    Inspect,
}

public enum InspectionState
{
    Open,
    Passed,
}

public enum InspectionTrigger
{
    Pass,
}

/// <summary>What a parcel tells its consignment when it ends: the label it was sealed with.</summary>
public sealed record ParcelResult
{
    public string Label { get; init; } = "";
}

public sealed record SealInput
{
    [MinLength(1)]
    public string Label { get; init; } = "";
}

/// <summary>
/// A user's consignment whose <c>Packing</c> state spawns a <see cref="ParcelMachine"/> for the same user: the parcel
/// sealed sends the consignment to <c>Sent</c> with its label, a dropped parcel to <c>Lost</c>, and a parcel that
/// disappears to <c>Recalled</c>. <c>Abandon</c> leaves <c>Packing</c>, which removes the parcel.
/// </summary>
public sealed class ConsignmentMachine : Machine<ConsignmentState, ConsignmentTrigger>
{
    public const string Id = "consignment";

    public sealed record OpenContext
    {
        public Guid? ParcelId { get; init; }

        public string Label { get; init; } = "";
    }

    protected override void Configure(IMachineBuilder<ConsignmentState, ConsignmentTrigger> m)
    {
        m.Id(Id).Version(1).StartsAt(ConsignmentState.Open, Empty);

        m.In(ConsignmentState.Open)
            .Context<OpenContext>()
            .On(ConsignmentTrigger.Pack)
            .To(ConsignmentState.Packing);

        m.In(ConsignmentState.Packing)
            .Context<OpenContext>()
            .Spawns<ParcelMachine, OpenContext, ParcelResult>(c => c.ParcelId)
            .OnDone(
                ConsignmentState.Sent,
                reduce: Set((OpenContext c) => c.Label).FromInput((ParcelResult r) => r.Label)
            )
            .OnFailed(ConsignmentState.Lost)
            .OnCancelled(ConsignmentState.Recalled)
            .On(ConsignmentTrigger.Abandon)
            .To(ConsignmentState.Abandoned);

        m.In(ConsignmentState.Sent).Context<OpenContext>();
        m.In(ConsignmentState.Lost).Context<OpenContext>();
        m.In(ConsignmentState.Recalled).Context<OpenContext>();
        m.In(ConsignmentState.Abandoned).Context<OpenContext>();
    }

    /// <summary>A snapshot of this machine in <paramref name="state"/>, as host code seeds it.</summary>
    public static Snapshot At(ConsignmentState state) =>
        new()
        {
            Machine = Id,
            Version = 1,
            State = state.ToString(),
            Context = Empty(),
        };

    private static JsonObject Empty() => new() { ["parcelId"] = null, ["label"] = "" };
}

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

public sealed record InspectionResult;

/// <summary>A parcel's own child: passed, it ends.</summary>
public sealed class InspectionMachine : Machine<InspectionState, InspectionTrigger>
{
    public const string Id = "inspection";

    protected override void Configure(IMachineBuilder<InspectionState, InspectionTrigger> m)
    {
        m.Id(Id).Version(1).StartsAt(InspectionState.Open, () => new JsonObject());
        m.In(InspectionState.Open).Context().On(InspectionTrigger.Pass).To(InspectionState.Passed);
        m.In(InspectionState.Passed).Context().Ends();
    }
}

/// <summary><see cref="ConsignmentMachine"/>, owned by the system, spawning a system parcel.</summary>
public sealed class SystemConsignmentMachine : Machine<ConsignmentState, ConsignmentTrigger>
{
    public const string Id = "system-consignment";

    protected override void Configure(IMachineBuilder<ConsignmentState, ConsignmentTrigger> m)
    {
        m.Id(Id)
            .Version(1)
            .SystemOwned()
            .StartsAt(
                ConsignmentState.Open,
                () => new JsonObject { ["parcelId"] = null, ["label"] = "" }
            );

        m.In(ConsignmentState.Open)
            .Context<ConsignmentMachine.OpenContext>()
            .On(ConsignmentTrigger.Pack)
            .To(ConsignmentState.Packing);

        m.In(ConsignmentState.Packing)
            .Context<ConsignmentMachine.OpenContext>()
            .Spawns<SystemParcelMachine, ConsignmentMachine.OpenContext, ParcelResult>(c =>
                c.ParcelId
            )
            .OnDone(
                ConsignmentState.Sent,
                reduce: Set((ConsignmentMachine.OpenContext c) => c.Label)
                    .FromInput((ParcelResult r) => r.Label)
            )
            .OnFailed(ConsignmentState.Lost)
            .OnCancelled(ConsignmentState.Recalled);

        m.In(ConsignmentState.Sent).Context<ConsignmentMachine.OpenContext>();
        m.In(ConsignmentState.Lost).Context<ConsignmentMachine.OpenContext>();
        m.In(ConsignmentState.Recalled).Context<ConsignmentMachine.OpenContext>();
    }
}

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
