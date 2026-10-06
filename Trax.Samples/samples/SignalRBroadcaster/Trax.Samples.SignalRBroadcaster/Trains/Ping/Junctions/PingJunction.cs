using Microsoft.Extensions.Logging;
using Trax.Core.Exceptions;
using Trax.Core.Junction;

namespace Trax.Samples.SignalRBroadcaster.Trains.Ping.Junctions;

public class PingJunction(ILogger<PingJunction> logger) : Junction<PingInput, PingOutput>
{
    public override async Task<PingOutput> Run(PingInput input)
    {
        logger.LogInformation("[{Source}] ping ({Outcome})", input.Source, input.Outcome);
        await Task.Delay(input.DelayMs);

        return input.Outcome switch
        {
            PingOutcome.FailForClients => throw new TrainException(
                "The ping target did not answer."
            ),
            PingOutcome.FailUnexpectedly => throw new InvalidOperationException(
                "Socket to 10.0.4.17:5432 refused (user=svc_ping)."
            ),
            _ => new PingOutput { Source = input.Source, PingedAt = DateTime.UtcNow },
        };
    }
}
