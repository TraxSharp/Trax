namespace Trax.StateMachine;

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
