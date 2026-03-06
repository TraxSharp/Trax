using Microsoft.Extensions.Logging;
using Trax.Core.Step;

namespace Trax.Samples.EnergyHub.Trains.SolarProduction.MonitorSolarProduction.Steps;

public class ReadSolarSensorsStep(ILogger<ReadSolarSensorsStep> logger)
    : Step<MonitorSolarProductionInput, MonitorSolarProductionInput>
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
