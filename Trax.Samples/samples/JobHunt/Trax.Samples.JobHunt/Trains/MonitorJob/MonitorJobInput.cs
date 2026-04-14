using Trax.Effect.Models.Manifest;

namespace Trax.Samples.JobHunt.Trains.MonitorJob;

public record MonitorJobInput : IManifestProperties
{
    public Guid JobId { get; init; }
}
