namespace Trax.Effect.StateMachine;

/// <summary>Who may fire a machine's trigger, and through which path.</summary>
public enum TriggerKind
{
    /// <summary>A client fires it through an advance.</summary>
    Client,

    /// <summary>It runs the machine's irreversible effect, so only the effect runner fires it.</summary>
    EffectBound,

    /// <summary>An invoked train's outcome, which only the outcome path applies.</summary>
    Outcome,

    /// <summary>A state's timer, which only the server fires when it is due.</summary>
    Timer,
}

/// <summary>One trigger a machine declares, with its kind and the CLR type of its input.</summary>
/// <param name="Name">The trigger's wire name.</param>
/// <param name="Kind">Who may fire it.</param>
/// <param name="InputType">The record declared with <c>WithInput</c>, or <c>null</c> when it takes no input.</param>
public sealed record MachineTrigger(string Name, TriggerKind Kind, Type? InputType);

/// <summary>One state a machine declares, with the record its context is declared with.</summary>
/// <param name="Name">The state's wire name.</param>
/// <param name="ContextType">
/// The record the state declares with <c>Context&lt;TContext&gt;()</c>, or <c>null</c> when it declares an empty
/// context (<c>Context()</c>) or none.
/// </param>
public sealed record MachineState(string Name, Type? ContextType);
