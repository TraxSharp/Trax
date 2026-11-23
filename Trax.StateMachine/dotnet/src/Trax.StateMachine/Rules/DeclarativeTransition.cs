using System.Text.Json.Nodes;

namespace Trax.StateMachine;

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
