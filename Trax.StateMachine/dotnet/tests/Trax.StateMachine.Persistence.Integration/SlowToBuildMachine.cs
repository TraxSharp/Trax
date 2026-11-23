using System.Text.Json.Nodes;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Testing;
using Trax.StateMachine.Persistence.Integration.Fakes;
using Trax.StateMachine.Persistence.Integration.Fixtures;
using Trax.StateMachine.Persistence.Mutations;

namespace Trax.StateMachine.Persistence.Integration;

/// <summary>
/// A declarative machine whose first <see cref="Configure"/> blocks until released, when made by
/// <see cref="Blocking"/>. The assembly scan registers it in every host built over this assembly, so the instance the
/// scan makes is released from the start: every host builds every machine it registers, and a blocked one would hold
/// each host for the whole wait.
/// </summary>
public sealed class SlowToBuildMachine : Machine<GateState, GateTrigger>
{
    public readonly ManualResetEventSlim Release = new(initialState: true);
    public readonly ManualResetEventSlim Entered = new(initialState: false);

    /// <summary>An instance whose first build waits for <see cref="Release"/>.</summary>
    public static SlowToBuildMachine Blocking()
    {
        var machine = new SlowToBuildMachine();
        machine.Release.Reset();
        return machine;
    }

    protected override void Configure(IMachineBuilder<GateState, GateTrigger> m)
    {
        Entered.Set();
        Release.Wait(TimeSpan.FromSeconds(10));
        m.Id("slow").Version(1).StartsAt(GateState.A, () => new JsonObject());
        m.In(GateState.A).Context().On(GateTrigger.Go).To(GateState.B);
        m.In(GateState.B).Context();
    }
}
