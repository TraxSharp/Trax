namespace Trax.Samples.SignalRBroadcaster.Trains.Ping;

public record PingInput
{
    public required string Source { get; init; }

    public PingOutcome Outcome { get; init; } = PingOutcome.Succeed;

    public int DelayMs { get; init; } = 250;
}
