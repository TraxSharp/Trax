using Microsoft.Extensions.Logging;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Samples.Scheduling.Trains.AlertRateSpike;
using Trax.Scheduler.Services.DormantDependentContext;

namespace Trax.Samples.Scheduling.Trains.RefreshExchangeRates.Junctions;

/// <summary>
/// Activates the dormant <c>alert-rate-spike</c> dependent when the move is past the threshold.
/// <see cref="IDormantDependentContext"/> is bound to the manifest whose run this is, so it can
/// activate only dormant dependents declared under that manifest. Run outside the scheduler (on
/// the train bus), the context has no manifest and the activation logs a warning and does nothing.
/// </summary>
public class FlagSpikeJunction(IDormantDependentContext dormants, ILogger<FlagSpikeJunction> logger)
    : Junction<(RefreshExchangeRatesInput Input, RateReading Reading), RefreshExchangeRatesOutput>
{
    public override async Task<RefreshExchangeRatesOutput> Run(
        (RefreshExchangeRatesInput Input, RateReading Reading) input
    )
    {
        var (settings, reading) = input;
        var spike = Math.Abs(reading.ChangePercent) > settings.SpikeThresholdPercent;

        if (spike)
        {
            logger.LogWarning(
                "{Currency} moved {Change}%, past {Threshold}%: activating {Alert}",
                reading.BaseCurrency,
                reading.ChangePercent,
                settings.SpikeThresholdPercent,
                ManifestNames.AlertRateSpike
            );

            await dormants.ActivateAsync<IAlertRateSpikeTrain, AlertRateSpikeInput, Unit>(
                ManifestNames.AlertRateSpike,
                new AlertRateSpikeInput
                {
                    BaseCurrency = reading.BaseCurrency,
                    ChangePercent = reading.ChangePercent,
                }
            );
        }

        return new RefreshExchangeRatesOutput(reading.BaseCurrency, reading.ChangePercent, spike);
    }
}
