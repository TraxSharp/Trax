namespace Trax.Scheduler.Services.Operations;

/// <param name="Id">Database ID of the manifest group.</param>
/// <param name="Name">Group name as stored on the row.</param>
/// <param name="IsHighlighted">True for the focal group; the UI uses this to render it differently.</param>
public record DependencyGraphNode(long Id, string Name, bool IsHighlighted)
{
    /// <summary>Whether the group is enabled: a disabled group's manifests are not queued.</summary>
    public bool IsEnabled { get; init; }
}
