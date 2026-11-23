using System.Text.Json.Nodes;

namespace Trax.StateMachine.Persistence;

/// <summary>A timed state's timer as the IR declares it: a fixed duration, or one a context field holds within bounds.</summary>
/// <param name="State">The timed state.</param>
/// <param name="Trigger">The timer's trigger.</param>
/// <param name="Target">The state the timer moves the instance to.</param>
internal sealed record MachineTimer(string State, string Trigger, string Target)
{
    /// <summary>The fixed duration (ISO-8601), or null when a context field holds it.</summary>
    public string? After { get; init; }

    /// <summary>The context field that holds the duration, or null for a fixed one.</summary>
    public string? Field { get; init; }

    /// <summary>The shortest duration the field may hold (ISO-8601); null for a fixed one.</summary>
    public string? Min { get; init; }

    /// <summary>The longest duration the field may hold (ISO-8601); null for a fixed one.</summary>
    public string? Max { get; init; }

    /// <summary>The duration as one line: the fixed duration, or the field with its bounds.</summary>
    public string Duration => After ?? (Field is not null ? $"{Field} ({Min} to {Max})" : "?");
}
