using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.Samples.EnergyHub.Trains.SolarProduction.MonitorSolarProduction.Junctions;

public class ReadSolarSensorsJunction(ILogger<ReadSolarSensorsJunction> logger)
    : Junction<MonitorSolarProductionInput, MonitorSolarProductionInput>
{
    public override async Task<MonitorSolarProductionInput> Run(MonitorSolarProductionInput input)
    {
        logger.LogInformation(
            "[{ArrayId}] Reading solar panel sensors across {Region} zone",
            input.ArrayId,
            input.Region
        );

        await Task.Delay(250);

        logger.LogInformation(
            "[{ArrayId}] Sensor data collected — 48 panels reporting nominal output",
            input.ArrayId
        );

        return input;
    }
}
