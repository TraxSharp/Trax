using Trax.Samples.SignalRBroadcaster.Trains.Ping;

namespace Trax.Samples.SignalRBroadcaster;

/// <summary>The body of <c>POST /pings</c>.</summary>
public record PingRequest(PingOutcome Outcome = PingOutcome.Succeed);
