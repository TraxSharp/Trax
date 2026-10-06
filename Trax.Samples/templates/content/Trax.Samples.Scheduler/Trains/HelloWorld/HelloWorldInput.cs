using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Scheduler.Trains.HelloWorld;

/// <summary>
/// The train's input. IManifestProperties lets the scheduler store it in a manifest, so a
/// train has to take an IManifestProperties input to be scheduled.
/// </summary>
public record HelloWorldInput : IManifestProperties
{
    /// <summary>
    /// The name to greet in the train.
    /// </summary>
    public string Name { get; init; } = "World";
}
