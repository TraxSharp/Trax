using Trax.Effect.Models.Manifest;

namespace Trax.Samples.PersistedOperations.Trains.Greeting.Greet;

/// <summary>
/// Input for the greet train: the name to address in the response.
/// </summary>
public record GreetInput : IManifestProperties
{
    public required string Name { get; init; }
}
