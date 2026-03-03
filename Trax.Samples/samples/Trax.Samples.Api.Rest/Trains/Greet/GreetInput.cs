using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Api.Rest.Trains.Greet;

public record GreetInput : IManifestProperties
{
    public string Name { get; init; } = "World";
}
