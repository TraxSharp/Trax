using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Persistence.Integration.Fakes;
using Trax.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.StateMachine.Persistence.Integration;

/// <summary>
/// A declarative machine whose state is checked by a delegate (<c>Holds</c>), which the IR cannot carry. Its IR
/// export is refused by design, so it has no schema hash.
/// </summary>
public sealed class UnexportableMachine : Machine<GateState, GateTrigger>
{
    public const string Id = "unexportable";

    protected override void Configure(IMachineBuilder<GateState, GateTrigger> m)
    {
        m.Id(Id).Version(1).StartsAt(GateState.A, () => new JsonObject());
        m.In(GateState.A).Context().On(GateTrigger.Go).To(GateState.B);
        m.In(GateState.B).Holds(_ => null);
    }
}
