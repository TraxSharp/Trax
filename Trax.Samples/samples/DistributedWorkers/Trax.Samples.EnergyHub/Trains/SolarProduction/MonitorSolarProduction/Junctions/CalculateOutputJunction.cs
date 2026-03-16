using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.Samples.EnergyHub.Trains.SolarProduction.MonitorSolarProduction.Junctions;

public class CalculateOutputJunction(ILogger<CalculateOutputJunction> logger)
    : Junction<MonitorSolarProductionInput, MonitorSolarProductionOutput>
{
    public override async Task<MonitorSolarProductionOutput> Run(MonitorSolarProductionInput input)
    {
        logger.LogInformation(
            "[{ArrayId}] Calculating energy output and panel efficiency",
            input.ArrayId
        );

        await Task.Delay(150);

        var totalKwh = 142.7;
        var efficiency = 0.89;

        logger.LogInformation(
            "[{ArrayId}] Solar production: {Kwh} kWh at {Efficiency:P0} efficiency",
            input.ArrayId,
            totalKwh,
            efficiency
        );

        return new MonitorSolarProductionOutput
        {
            ArrayId = input.ArrayId,
            TotalKwh = totalKwh,
            Efficiency = efficiency,
        };
    }
}
