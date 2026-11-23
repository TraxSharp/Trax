using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Trax.Effect.Attributes;
using Trax.StateMachine.Features;

namespace Trax.StateMachine.Persistence.FromIr;

/// <summary>
/// A machine served from its IR file and its entry in the host file, with no C# class of its own (ADR
/// <c>Trax.Docs/adr/0058</c>). It is closed over the enums <see cref="IrTypes"/> emitted for it, and its
/// <see cref="Configure"/> replays the IR into the same builder a hand-written machine declares itself with, so from
/// there it is served like any other. <c>IrMachines.Create</c> builds one and refuses it if what it exports is not
/// the file.
/// </summary>
internal abstract class IrServedMachine<TState, TTrigger>(IrReplay replay)
    : Machine<TState, TTrigger>,
        IDeclaredSystemKey
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    /// <inheritdoc/>
    public string? KeyProblem(MachineKey key) => replay.KeyProblem(key);

    /// <inheritdoc/>
    public override string? Corpus => replay.Corpus;

    /// <inheritdoc/>
    protected override void Configure(IMachineBuilder<TState, TTrigger> machine) =>
        replay.Configure(machine);
}
