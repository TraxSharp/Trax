using System.ComponentModel.DataAnnotations;
using System.Text.Json.Nodes;
using static Trax.StateMachine.Rules;

namespace Trax.StateMachine.Persistence.Integration.Fakes;

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
