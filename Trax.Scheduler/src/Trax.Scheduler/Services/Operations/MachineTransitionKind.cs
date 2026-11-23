namespace Trax.Scheduler.Services.Operations;

/// <summary>What takes a transition.</summary>
public enum MachineTransitionKind
{
    /// <summary>A command a caller sends.</summary>
    Command,

    /// <summary>The state's timer, which only the server fires.</summary>
    Timer,

    /// <summary>The outcome of the train the state invoked, or of the machine it spawned.</summary>
    Outcome,
}
