using System.ComponentModel;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Utils;

namespace Trax.StateMachine.Persistence;

/// <summary>The machine-agnostic handle a host discovers and the registry keys on.</summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/machine#members">SDK reference</seealso>
public interface IMachine
{
    /// <summary>The machine's stable id (from the fluent <c>Id(...)</c>).</summary>
    string Name { get; }

    /// <summary>Whether the machine binds an irreversible exactly-once effect.</summary>
    bool HasEffect { get; }

    /// <summary>
    /// Export this machine's neutral IR (the formalized machine.json) as canonical single-line JSON: the
    /// single-source artifact every frontend generator consumes. Requires a declaratively-authored machine
    /// (<c>.Context</c>/<c>.When</c>/<c>.Reduce</c>); a raw-delegate machine throws, because its guards and
    /// reducers are opaque closures with no exportable data. This is the in-process entry point the
    /// <c>trax machine</c> CLI calls.
    /// </summary>
    string ExportIr();

    /// <summary>
    /// A stable content hash (lowercase hex SHA-256) of this machine's exported IR (<see cref="ExportIr"/>) —
    /// the cross-language identity of its behavioural contract. The generated frontend twin embeds the same
    /// hash, computed from the same committed IR, so a client and the server can detect at runtime that they
    /// are running different machine definitions (version skew) and refuse to silently disagree on a transition.
    /// </summary>
    string? SchemaHash { get; }

    /// <summary>
    /// Every trigger the machine declares, with who may fire it (<see cref="TriggerKind"/>) and the record its
    /// input is declared with (null when it takes none). A typed API is built from this list, so only the client
    /// triggers become operations. <see cref="Machine{TState,TTrigger}"/> implements it; any other implementation
    /// that does not throws <see cref="NotSupportedException"/>.
    /// </summary>
    IReadOnlyList<MachineTrigger> Triggers =>
        throw new NotSupportedException(
            $"{GetType().Name} does not list its triggers. Derive the machine from Machine<TState, TTrigger>."
        );

    /// <summary>
    /// Every state the machine declares, in the order its state enum declares them, with the record its context is
    /// declared with (null for an empty context). A typed API types an instance's state and context from this list.
    /// <see cref="Machine{TState,TTrigger}"/> implements it; any other implementation that does not throws
    /// <see cref="NotSupportedException"/>.
    /// </summary>
    IReadOnlyList<MachineState> States =>
        throw new NotSupportedException(
            $"{GetType().Name} does not list its states. Derive the machine from Machine<TState, TTrigger>."
        );

    /// <summary>
    /// The record a client starts an instance with, as the machine's <c>Start&lt;TStartInput&gt;</c> declares it, or
    /// <see langword="null"/> when it declares no start and no client can create one. Never the context type.
    /// <see cref="Machine{TState,TTrigger}"/> implements it; any other implementation that does not throws
    /// <see cref="NotSupportedException"/>.
    /// </summary>
    Type? StartInputType =>
        throw new NotSupportedException(
            $"{GetType().Name} does not describe its start. Derive the machine from Machine<TState, TTrigger>."
        );

    /// <summary>
    /// The machine's committed differential corpus (the TypeScript oracle's golden JSON), or null if it ships
    /// none. A host replays it at startup via <see cref="SelfCheck"/> to prove the running C# engine still
    /// reproduces the frontend twin's behaviour. A machine that carries a corpus loads it (e.g. from an
    /// embedded resource).
    /// </summary>
    string? Corpus { get; }

    /// <summary>
    /// Replay this machine's <see cref="Corpus"/> through its own engine and return one human-readable diff per
    /// case it fails to reproduce — empty means exact agreement (and empty when the machine ships no corpus).
    /// This is the same proof the differential test runs, callable at startup as a self-check.
    /// </summary>
    IReadOnlyList<string> SelfCheck();

    /// <summary>
    /// Build the draft service for a request's store (threading committed states and the effect-claim reset). A
    /// service built here cannot <c>start</c> an instance and applies no per-user cap; a host gets one that can from
    /// <see cref="ISnapshotMachineRegistry.Service"/>. <paramref name="draftTtl"/> is not read: the draft sweep
    /// expires drafts.
    /// </summary>
    ISnapshotDraftService CreateService(
        ISnapshotStore store,
        IEffectClaimStore? claims,
        TimeSpan? draftTtl = null
    );

    /// <summary>Build the exactly-once effect runner (resolving the effect from the container), or null if none.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    ISnapshotEffectRunner? CreateEffectRunner(
        ISnapshotDraftService service,
        IdempotentEffect idempotent,
        IServiceProvider services
    );
}
