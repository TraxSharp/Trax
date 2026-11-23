namespace Trax.Scheduler.Services.Operations;

/// <summary>One transition of a <see cref="MachineDescription"/>.</summary>
/// <param name="From">The state it leaves.</param>
/// <param name="Command">
/// What takes it: the command's name, the timer's trigger, or the outcome trigger (<c>Building.done</c>,
/// <c>.failed</c>, <c>.cancelled</c>).
/// </param>
/// <param name="To">The state it enters.</param>
/// <param name="Kind">Whether a command, a timer or an outcome takes it.</param>
public record MachineTransitionDescription(
    string From,
    string Command,
    string To,
    MachineTransitionKind Kind
)
{
    /// <summary>The guard's message, or the name of its rule; null when the transition is unguarded.</summary>
    public string? Guard { get; init; }

    /// <summary>
    /// The key prefix of the effect the transition runs exactly once, or null when it runs none.
    /// </summary>
    public string? Effect { get; init; }

    /// <summary>
    /// For a timer, how long its state is held: an ISO-8601 duration, or the context field that holds it with its
    /// bounds. Null for a command or an outcome.
    /// </summary>
    public string? After { get; init; }
}
