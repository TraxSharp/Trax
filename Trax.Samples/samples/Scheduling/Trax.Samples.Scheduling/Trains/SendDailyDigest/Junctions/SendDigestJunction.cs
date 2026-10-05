using Microsoft.Extensions.Logging;
using Trax.Core.Functional;
using Trax.Core.Junction;

namespace Trax.Samples.Scheduling.Trains.SendDailyDigest.Junctions;

public class SendDigestJunction(ILogger<SendDigestJunction> logger)
    : Junction<SendDailyDigestInput, Unit>
{
    public override Task<Unit> Run(SendDailyDigestInput input)
    {
        logger.LogInformation("Sent the daily digest to {Audience}", input.Audience);
        return Task.FromResult(Unit.Default);
    }
}
