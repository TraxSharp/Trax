using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

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
