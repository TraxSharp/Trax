namespace Trax.StateMachine;

/// <summary>Where a rule reads its field from: the snapshot's context, or the trigger's input.</summary>
public enum RuleSource
{
    /// <summary>The snapshot's context object.</summary>
    Context,

    /// <summary>The trigger's input; absent when the trigger was fired without input.</summary>
    Input,
}
