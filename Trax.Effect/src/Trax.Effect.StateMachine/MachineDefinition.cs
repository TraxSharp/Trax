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

    /// <summary>
    /// The states that invoke a train, keyed by state, with where each outcome goes. Empty for a machine that
    /// invokes nothing. Only <see cref="SnapshotMachine{TState,TTrigger}.ApplyOutcome"/> reaches these edges;
    /// <see cref="SnapshotMachine{TState,TTrigger}.Advance"/> never fires them.
    /// </summary>
    internal IReadOnlyDictionary<TState, InvokeDefinition<TState>> Invokes { get; init; } =
        new Dictionary<TState, InvokeDefinition<TState>>();

    /// <summary>
    /// The schema each trigger's input is checked against before any guard reads it; a mismatch is
    /// <c>invalid-input</c>. <see cref="ContextSchema.Empty"/> accepts only no input or <c>{}</c>. A trigger with
    /// no entry is not checked, which is only the case on a machine whose guards and reducers are raw delegates
    /// reading the input themselves. Built by <see cref="MachineBuilder{TState,TTrigger}.Build"/>.
    /// </summary>
    internal IReadOnlyDictionary<TTrigger, ContextSchema> InputSchemas { get; init; } =
        new Dictionary<TTrigger, ContextSchema>();

    /// <summary>
    /// The states with a timer, keyed by state. Empty for a machine with none. Only
    /// <see cref="SnapshotMachine{TState,TTrigger}.AdvanceTimer"/> fires one; <see cref="SnapshotMachine{TState,TTrigger}.Advance"/>
    /// refuses a timer trigger as <c>timer-bound</c>.
    /// </summary>
    internal IReadOnlyDictionary<TState, TimerDefinition<TState>> Timers { get; init; } =
        new Dictionary<TState, TimerDefinition<TState>>();

    /// <summary>Builds the snapshot a brand-new instance begins with.</summary>
    public Snapshot CreateInitialSnapshot()
    {
        var context = CreateInitialContext();
        return new()
        {
            Machine = Id,
            Version = Version,
            State = InitialState.ToString(),
            Context = context,
            Timer = EnteringTimer(InitialState, context).Timer,
        };
    }

    /// <summary>
    /// What entering <paramref name="state"/> with <paramref name="context"/> does to the row's timer: arms the state's
    /// timer from now, or clears it when the state has none. A duration the context cannot give (missing, not an
    /// ISO-8601 duration, outside its bounds) is the <c>Problem</c>, and the timer is then null.
    /// </summary>
    internal (SnapshotTimer? Timer, string? Problem) EnteringTimer(TState state, JsonObject context)
    {
        if (!Timers.TryGetValue(state, out var timer))
            return (SnapshotTimer.None, null);
        var (duration, problem) = timer.DurationFor(context);
        return problem is null
            ? (new SnapshotTimer(duration, Restart: true), null)
            : (null, problem);
    }

    internal string? ValidateContext(TState state, JsonObject context) =>
        ContextValidators.TryGetValue(state, out var validate) ? validate(context) : null;
}
