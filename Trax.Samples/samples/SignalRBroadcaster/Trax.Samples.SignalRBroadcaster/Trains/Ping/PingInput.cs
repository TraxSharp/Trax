namespace Trax.Samples.SignalRBroadcaster.Trains.Ping;

/// <summary>How a ping run ends, so the page can show every kind of event.</summary>
public enum PingOutcome
{
    /// <summary>The run completes.</summary>
    Succeed,

    /// <summary>
    /// The run fails with a <see cref="Trax.Core.Exceptions.TrainException"/>, whose message is
    /// written for whoever watches the run.
    /// </summary>
    FailForClients,

    /// <summary>
    /// The run fails with an ordinary exception whose message carries internal detail, the kind a
    /// client must not see.
    /// </summary>
    FailUnexpectedly,
}

public record PingInput
{
    public required string Source { get; init; }

    public PingOutcome Outcome { get; init; } = PingOutcome.Succeed;

    public int DelayMs { get; init; } = 250;
}
