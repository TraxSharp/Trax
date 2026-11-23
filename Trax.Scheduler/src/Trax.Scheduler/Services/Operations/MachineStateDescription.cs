namespace Trax.Scheduler.Services.Operations;

/// <summary>One state of a <see cref="MachineDescription"/>.</summary>
/// <param name="Name">The state's name.</param>
public record MachineStateDescription(string Name)
{
    /// <summary>True for the state an instance starts in.</summary>
    public bool IsInitial { get; init; }

    /// <summary>True when no transition leaves the state: an instance that reaches it stays there.</summary>
    public bool IsFinal { get; init; }

    /// <summary>True when the state is committed: once the server puts an instance there, it cannot be discarded.</summary>
    public bool IsCommitted { get; init; }

    /// <summary>The train the state invokes on entry (its interface's full name), or null when it invokes none.</summary>
    public string? InvokesTrain { get; init; }

    /// <summary>The machine the state spawns on entry (its id), or null when it spawns none.</summary>
    public string? SpawnsMachine { get; init; }
}
