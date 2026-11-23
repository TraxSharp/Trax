using Trax.Scheduler.Services.Operations;

namespace Trax.Api.DTOs;

/// <summary>One state of a <see cref="MachineDefinition"/>.</summary>
/// <param name="Name">The state's name.</param>
public record MachineDefinitionState(string Name)
{
    /// <summary>True for the state an instance starts in.</summary>
    public bool IsInitial { get; init; }

    /// <summary>True when no transition leaves the state.</summary>
    public bool IsFinal { get; init; }

    /// <summary>True when the state is committed: once the server puts an instance there, it cannot be discarded.</summary>
    public bool IsCommitted { get; init; }

    /// <summary>The train the state invokes on entry (its interface's full name), or null.</summary>
    public string? InvokesTrain { get; init; }

    /// <summary>The machine the state spawns on entry (its id), or null.</summary>
    public string? SpawnsMachine { get; init; }
}
