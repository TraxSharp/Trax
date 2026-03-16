using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.Samples.EnergyHub.Trains.Microgrid.OptimizeMicrogrid.Junctions;

public class ApplyDistributionJunction(ILogger<ApplyDistributionJunction> logger)
    : Junction<OptimizeMicrogridInput, Unit>
{
    public override async Task<Unit> Run(OptimizeMicrogridInput input)
    {
        logger.LogInformation(
            "[{GridZone}] Applying optimized energy distribution via solid-state converters",
            input.GridZone
        );

        await Task.Delay(250);

        logger.LogInformation(
            "[{GridZone}] Distribution applied — 60% solar direct, 25% battery, 15% grid import",
            input.GridZone
        );

        return Unit.Default;
    }
}
