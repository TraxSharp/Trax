using Trax.Effect.Models.Manifest;

namespace Trax.Samples.Scheduling.Trains.SendLaunchAnnouncement;

public record SendLaunchAnnouncementInput : IManifestProperties
{
    public string Message { get; init; } = "The spring catalog is live.";
}
