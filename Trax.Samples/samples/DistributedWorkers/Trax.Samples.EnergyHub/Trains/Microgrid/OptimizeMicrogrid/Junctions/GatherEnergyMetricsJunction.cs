using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.Samples.EnergyHub.Trains.Microgrid.OptimizeMicrogrid.Junctions;

public class GatherEnergyMetricsJunction(ILogger<GatherEnergyMetricsJunction> logger)
    : Junction<OptimizeMicrogridInput, OptimizeMicrogridInput>
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
