using Trax.Effect.StateMachine.Persistence;

namespace Trax.Effect.StateMachine.Tests.Fakes;

/// <summary>
/// A <see cref="Machine{TState,TTrigger}"/> declared by a configure action, so the same declaration a fake builds
/// with <see cref="MachineBuilder{TState,TTrigger}"/> can be read through <see cref="IMachine"/> (its trigger list,
/// the census and the startup checks).
/// </summary>
public sealed class ConfiguredMachine<TState, TTrigger>(
    Action<IMachineBuilder<TState, TTrigger>> configure
) : Machine<TState, TTrigger>
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    protected override void Configure(IMachineBuilder<TState, TTrigger> machine) =>
        configure(machine);
}
