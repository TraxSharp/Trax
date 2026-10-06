using Microsoft.Extensions.Logging;
using Trax.Core.Functional;
using Trax.Core.Junction;

namespace Trax.Samples.Scheduling.Trains.AlertRateSpike.Junctions;

public class NotifyTreasuryJunction(ILogger<NotifyTreasuryJunction> logger)
    : Junction<AlertRateSpikeInput, Unit>
{
    public override Task<Unit> Run(AlertRateSpikeInput input)
    {
        logger.LogWarning(
            "Treasury alerted: {Currency} moved {Change}%",
            input.BaseCurrency,
            input.ChangePercent
        );
        return Task.FromResult(Unit.Default);
    }
}
