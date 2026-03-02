using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Server.Trains.HelloWorld;

/// <summary>
/// Input for the HelloWorld train.
/// Implements IManifestProperties to enable serialization for scheduled jobs.
/// </summary>
public record HelloWorldInput : IManifestProperties
{
    /// <summary>
    /// The name to greet in the train.
    /// </summary>
    public string Name { get; init; } = "World";
}
