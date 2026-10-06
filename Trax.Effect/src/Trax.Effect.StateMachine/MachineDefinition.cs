using System.Text.Json.Nodes;

namespace Trax.Effect.StateMachine;

/// <summary>
/// The result of migrating a snapshot one version forward: the (possibly transformed) state token and
/// context for the next version.
/// </summary>
public sealed record MigrationResult(string State, JsonObject Context);

/// <summary>
/// The static description of a machine — its states, edges, and per-state context rules. This
/// lives in <b>code</b> (not in the snapshot): the snapshot is data, the definition is the program
/// that interprets it. The C# definition here and the TypeScript definition of the same machine are
/// kept in agreement by the shared conformance fixtures, which is why no codegen of behavior is needed.
/// </summary>
public sealed class MachineDefinition<TState, TTrigger>
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    /// <summary>Stable identifier written into every snapshot's <c>machine</c> field.</summary>
    public required string Id { get; init; }

    /// <summary>Definition version written into every snapshot's <c>version</c> field.</summary>
    public required int Version { get; init; }

    /// <summary>The state a brand-new instance starts in.</summary>
    public required TState InitialState { get; init; }

    /// <summary>Factory for a fresh initial context (a factory, not a value, so no JSON node is shared/re-parented).</summary>
    public required Func<JsonObject> CreateInitialContext { get; init; }

    /// <summary>Every edge in the machine.</summary>
    public required IReadOnlyList<TransitionDefinition<TState, TTrigger>> Transitions { get; init; }

    /// <summary>
    /// Per-state context validators. A validator returns <c>null</c> when the context is legal for
    /// that state, or a human-readable message when it is not. A state with no entry accepts any
    /// context. These are what make the discriminated-union guarantee real at the JSON boundary.
    /// </summary>
    public IReadOnlyDictionary<TState, Func<JsonObject, string?>> ContextValidators { get; init; } =
        new Dictionary<TState, Func<JsonObject, string?>>();

    /// <summary>
    /// Forward migrations keyed by the version they migrate <b>from</b>: entry <c>N</c> turns a
    /// version-<c>N</c> snapshot into a version-<c>N+1</c> one. On rehydrating an older snapshot the
    /// engine applies these in sequence up to <see cref="Version"/>. A gap in the chain (or a snapshot
    /// newer than the definition) is a <c>version-mismatch</c>. Empty = no migration; an old snapshot is
    /// rejected rather than silently misread.
    /// </summary>
    public IReadOnlyDictionary<
        int,
        Func<string, JsonObject, MigrationResult>
    > Migrations { get; init; } = new Dictionary<int, Func<string, JsonObject, MigrationResult>>();

    /// <summary>Builds the snapshot a brand-new instance begins with.</summary>
    public Snapshot CreateInitialSnapshot() =>
        new()
        {
            Machine = Id,
            Version = Version,
            State = InitialState.ToString(),
            Context = CreateInitialContext(),
        };

    internal string? ValidateContext(TState state, JsonObject context) =>
        ContextValidators.TryGetValue(state, out var validate) ? validate(context) : null;
}
