using Microsoft.Extensions.Logging;
using Trax.Core.Step;

namespace Trax.Samples.EnergyHub.Trains.GridTrading.TradeGridEnergy.Steps;

public class CalculateExcessStep(ILogger<CalculateExcessStep> logger)
    : Step<TradeGridEnergyInput, TradeGridEnergyInput>
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
