using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Persistence.Integration.Fakes;
using Trax.StateMachine.Persistence.Integration.Fixtures;

namespace Trax.StateMachine.Persistence.Integration;

/// <summary>
/// A fully declarative machine that should export, but whose initial context throws when the export reads it, when
/// made by <see cref="Broken"/>: a genuine failure to compute its hash, not a refusal by design. The assembly scan
/// registers it in every host built over this assembly, so the instance the scan makes is healthy.
/// </summary>
public sealed class BrokenExportMachine : Machine<GateState, GateTrigger>
{
    public const string Id = "broken-export";

    private bool _broken;

    /// <summary>An instance whose initial context throws.</summary>
    public static BrokenExportMachine Broken() => new() { _broken = true };

    protected override void Configure(IMachineBuilder<GateState, GateTrigger> m)
    {
        m.Id(Id)
            .Version(1)
            .StartsAt(
                GateState.A,
                () =>
                    _broken
                        ? throw new InvalidOperationException("the initial context is unavailable")
                        : new JsonObject()
            );
        m.In(GateState.A).Context().On(GateTrigger.Go).To(GateState.B);
        m.In(GateState.B).Context();
    }
}
