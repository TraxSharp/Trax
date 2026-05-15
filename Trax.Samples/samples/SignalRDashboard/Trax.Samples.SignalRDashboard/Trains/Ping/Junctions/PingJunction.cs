using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.Samples.SignalRDashboard.Trains.Ping.Junctions;

public class PingJunction(ILogger<PingJunction> logger) : Junction<PingInput, PingOutput>
{
    public override async Task<PingOutput> Run(PingInput input)
    {
        logger.LogInformation("[{Source}] ping (delay {DelayMs}ms)", input.Source, input.DelayMs);
        await Task.Delay(input.DelayMs);

        return new PingOutput { Source = input.Source, PingedAt = DateTime.UtcNow };
    }
}
