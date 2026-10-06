using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.Samples.EnergyHub.Trains.GridTrading.TradeGridEnergy.Junctions;

public class SubmitToUbossJunction(ILogger<SubmitToUbossJunction> logger)
    : Junction<TradeGridEnergyInput, TradeGridEnergyOutput>
{
    public override async Task<TradeGridEnergyOutput> Run(TradeGridEnergyInput input)
    {
        logger.LogInformation(
            "Submitting sell order to PTC UBOSS at ${Rate}/kWh",
            input.RatePerKwh
        );

        await Task.Delay(300);

        var kwhSold = 47.3;
        var revenue = (decimal)kwhSold * input.RatePerKwh;
        var transactionId = $"UBOSS-{DateTime.UtcNow:yyyyMMddHHmm}-{Guid.NewGuid():N}"[..32];

        logger.LogInformation(
            "UBOSS transaction complete — sold {Kwh} kWh for ${Revenue:F2} (tx: {TxId})",
            kwhSold,
            revenue,
            transactionId
        );

        return new TradeGridEnergyOutput
        {
            KwhSold = kwhSold,
            Revenue = revenue,
            UbossTransactionId = transactionId,
        };
    }
}
