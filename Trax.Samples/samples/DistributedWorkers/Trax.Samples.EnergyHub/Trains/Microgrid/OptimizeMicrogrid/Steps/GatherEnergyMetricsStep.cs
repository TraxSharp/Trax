using Microsoft.Extensions.Logging;
using Trax.Core.Step;

namespace Trax.Samples.EnergyHub.Trains.Microgrid.OptimizeMicrogrid.Steps;

public class GatherEnergyMetricsStep(ILogger<GatherEnergyMetricsStep> logger)
    : Step<OptimizeMicrogridInput, OptimizeMicrogridInput>
{
    public override async Task<OptimizeMicrogridInput> Run(OptimizeMicrogridInput input)
    {
        logger.LogInformation(
            "[{GridZone}] Gathering energy metrics — solar output, battery state, grid demand",
            input.GridZone
        );

        await Task.Delay(300);

        logger.LogInformation(
            "[{GridZone}] Metrics collected — solar: 142 kWh, battery: 67%, demand: 95 kWh",
            input.GridZone
        );

        return input;
    }
}
