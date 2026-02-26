using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Server.Workflows.HelloWorld;

/// <summary>
/// Input for the HelloWorld workflow.
/// Implements IManifestProperties to enable serialization for scheduled jobs.
/// </summary>
public record HelloWorldInput : IManifestProperties
{
    /// <summary>
    /// The name to greet in the workflow.
    /// </summary>
    public string Name { get; init; } = "World";
}
