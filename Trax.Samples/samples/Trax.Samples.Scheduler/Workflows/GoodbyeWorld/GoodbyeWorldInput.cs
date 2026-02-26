using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Scheduler.Workflows.GoodbyeWorld;

/// <summary>
/// Input for the GoodbyeWorld workflow.
/// Implements IManifestProperties to enable serialization for scheduled jobs.
/// </summary>
public record GoodbyeWorldInput : IManifestProperties
{
    /// <summary>
    /// The name to say goodbye to in the workflow.
    /// </summary>
    public string Name { get; init; } = "World";
}
