using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Scheduling.Trains.SendDailyDigest;

public record SendDailyDigestInput : IManifestProperties
{
    public string Audience { get; init; } = "subscribers";
}
