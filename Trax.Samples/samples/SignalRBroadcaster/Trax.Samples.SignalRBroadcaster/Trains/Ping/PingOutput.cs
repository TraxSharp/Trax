namespace Trax.Samples.SignalRBroadcaster.Trains.Ping;

public record PingOutput
{
    public required string Source { get; init; }
    public required DateTime PingedAt { get; init; }
}
