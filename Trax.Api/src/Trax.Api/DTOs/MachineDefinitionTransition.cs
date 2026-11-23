using Trax.Scheduler.Services.Operations;

namespace Trax.Api.DTOs;

/// <summary>One transition of a <see cref="MachineDefinition"/>.</summary>
/// <param name="From">The state it leaves.</param>
/// <param name="Command">What takes it: a command's name, the timer's trigger, or an outcome trigger (<c>Building.done</c>).</param>
/// <param name="To">The state it enters.</param>
/// <param name="Kind">Whether a command, a timer or an outcome takes it.</param>
public record MachineDefinitionTransition(
    string From,
    string Command,
    string To,
    MachineTransitionKind Kind
)
{
    /// <summary>The guard's message, or the name of its rule; null when unguarded.</summary>
    public string? Guard { get; init; }

    /// <summary>The key prefix of the effect the transition runs exactly once, or null.</summary>
    public string? Effect { get; init; }

    /// <summary>For a timer, how long its state is held (ISO-8601, or a context field with its bounds); null otherwise.</summary>
    public string? After { get; init; }
}
