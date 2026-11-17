using System.Text.Json.Nodes;

namespace Trax.StateMachine;

/// <summary>
/// The declarative layer of a machine, captured when it is authored with the declarative overloads: the
/// per-state context schemas and the per-transition rules, as data. The engine runs compiled delegates; this
/// is what the IR exporter emits. It is <see langword="null"/> on a machine authored with raw delegates.
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
    /// Null when the machine declares no <c>.Differential(...)</c>. An <see langword="init"/> property, not a positional
    /// parameter, so existing construction stays source-compatible.
    /// </summary>
    public DifferentialModel<TState, TTrigger>? Differential { get; init; }

    /// <summary>
    /// The states whose validator is a <c>Holds</c> delegate rather than one built from
    /// <c>Context</c>/<c>Requires</c>. The IR cannot carry a delegate, so the exporter refuses a machine with any.
    /// </summary>
    internal IReadOnlySet<TState> DelegateValidatedStates { get; init; } = new HashSet<TState>();
}
