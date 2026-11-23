using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Persistence.Integration.Fakes;
using Trax.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.StateMachine.Persistence.Integration;

/// <summary>
/// A declarative machine with one delegate guard beside its declared context: its IR export is refused by design,
/// so it has no schema hash.
/// </summary>
public sealed class MixedDelegateGuardMachine : Machine<GateState, GateTrigger>
{
    public const string Id = "mixed-delegate-guard";

    protected override void Configure(IMachineBuilder<GateState, GateTrigger> m)
    {
        m.Id(Id).Version(1).StartsAt(GateState.A, () => new JsonObject());
        m.In(GateState.A)
            .Context()
            .On(GateTrigger.Go)
            .When((ctx, _) => ctx.Count == 0)
            .To(GateState.B);
        m.In(GateState.B).Context();
    }
}
