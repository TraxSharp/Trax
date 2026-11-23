using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

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
