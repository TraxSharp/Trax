namespace Trax.Scheduler.Services.Operations;

/// <summary>One timed state's timer.</summary>
/// <param name="State">The timed state.</param>
/// <param name="Command">The timer's trigger.</param>
/// <param name="Target">The state the timer moves an instance to.</param>
public record MachineTimerDescription(string State, string Command, string Target)
{
    /// <summary>The fixed duration (ISO-8601), or null when a context field holds it.</summary>
    public string? After { get; init; }

    /// <summary>The context field that holds the duration, or null for a fixed one.</summary>
    public string? Field { get; init; }

    /// <summary>The shortest duration the field may hold (ISO-8601), or null for a fixed one.</summary>
    public string? Min { get; init; }

    /// <summary>The longest duration the field may hold (ISO-8601), or null for a fixed one.</summary>
    public string? Max { get; init; }
}
