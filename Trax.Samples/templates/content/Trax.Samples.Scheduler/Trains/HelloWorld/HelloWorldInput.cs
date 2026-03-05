using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Scheduler.Trains.HelloWorld;

public record HelloWorldInput : IManifestProperties
{
    /// <summary>
    /// The name to greet in the train.
    /// </summary>
    public string Name { get; init; } = "World";
}
