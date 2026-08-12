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
    IReadOnlyList<DeclarativeTransition<TState, TTrigger>> Transitions
)
    where TState : struct, Enum
    where TTrigger : struct, Enum;
