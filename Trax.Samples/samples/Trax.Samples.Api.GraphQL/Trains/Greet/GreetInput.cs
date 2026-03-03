using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Api.GraphQL.Trains.Greet;

public record GreetInput : IManifestProperties
{
    public string Name { get; init; } = "World";
}
