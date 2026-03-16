using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.Samples.EnergyHub.Trains.GridTrading.TradeGridEnergy.Junctions;

public class CalculateExcessJunction(ILogger<CalculateExcessJunction> logger)
    : Junction<TradeGridEnergyInput, TradeGridEnergyInput>
{
    public override async Task<TradeGridEnergyInput> Run(TradeGridEnergyInput input)
    {
        logger.LogInformation(
            "Calculating excess energy available for grid sale (max {MaxSell}% of battery)",
            input.MaxSellPercent
        );

        await Task.Delay(200);

        logger.LogInformation(
            "Excess available: 47.3 kWh from solar surplus + battery above threshold"
        );

        return input;
    }
}
