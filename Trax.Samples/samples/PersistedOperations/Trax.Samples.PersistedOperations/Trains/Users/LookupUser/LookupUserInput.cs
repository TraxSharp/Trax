using Trax.Effect.Models.Manifest;

namespace Trax.Samples.PersistedOperations.Trains.Users.LookupUser;

public record LookupUserInput : IManifestProperties
{
    public required string UserId { get; init; }
}
