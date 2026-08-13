using System.Text.Json.Nodes;

namespace Trax.Effect.StateMachine;

/// <summary>
/// One transition's declarative guard/reducer as DATA, captured alongside the compiled delegates the engine
/// runs. A null <see cref="Guard"/> means the edge is always taken; a null <see cref="Reduce"/> means the
/// context is carried forward.
/// </summary>
public sealed record DeclarativeTransition<TState, TTrigger>(
    TState From,
    TTrigger Trigger,
    TState To,
    Rule? Guard,
    Reduction? Reduce
)
    where TState : struct, Enum
    where TTrigger : struct, Enum;

/// <summary>
/// The declarative layer of a machine, captured when it is authored with the declarative overloads: the
/// per-state context schemas and the per-transition rules, as data. The engine runs compiled delegates; this
/// is what the IR exporter emits. It is <c>null</c> on a machine authored with raw delegates.
/// </summary>
public sealed record DeclarativeModel<TState, TTrigger>(
    IReadOnlyDictionary<TState, ContextSchema> ContextSchemas,
    IReadOnlyDictionary<TTrigger, ContextSchema> TriggerInputs,
    IReadOnlyList<DeclarativeTransition<TState, TTrigger>> Transitions,
    IReadOnlyDictionary<TState, Rule> StateInvariants
)
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    /// <summary>
    /// The differential fuzzing inputs authored on the machine, if any (see <see cref="DifferentialModel{TState,TTrigger}"/>).
    /// Null when the machine declares no <c>.Differential(...)</c>. An <c>init</c> property, not a positional
    /// parameter, so existing construction stays source-compatible.
    /// </summary>
    public DifferentialModel<TState, TTrigger>? Differential { get; init; }
}

/// <summary>
/// The differential fuzzing inputs authored on a machine (test-only) with <c>.Differential(...)</c>:
/// representative per-trigger input <see cref="Samples"/>, per-state <see cref="Seeds"/> contexts (BFS start
/// points), and dense probe <see cref="Contexts"/> (crossed with every state). The IR exporter emits these so
/// the cross-language differential harness enumerates off the one C# source, with no hand-written machine.json.
/// The harness always adds a no-input case per trigger, so an explicit empty (<c>{}</c>) sample is distinct.
/// </summary>
public sealed record DifferentialModel<TState, TTrigger>(
    IReadOnlyDictionary<TTrigger, IReadOnlyList<JsonNode>> Samples,
    IReadOnlyDictionary<TState, JsonNode> Seeds,
    IReadOnlyList<JsonNode> Contexts
)
    where TState : struct, Enum
    where TTrigger : struct, Enum
{
    public bool IsEmpty => Samples.Count == 0 && Seeds.Count == 0 && Contexts.Count == 0;
}
