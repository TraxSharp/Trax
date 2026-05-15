using Trax.Effect.Models.Manifest;

namespace Trax.Samples.SignalRDashboard.Trains.Ping;

public record PingInput : IManifestProperties
{
    public required string Source { get; init; }
    public int DelayMs { get; init; } = 250;
}
