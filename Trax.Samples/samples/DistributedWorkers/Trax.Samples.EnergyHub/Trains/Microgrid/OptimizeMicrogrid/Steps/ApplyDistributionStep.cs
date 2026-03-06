using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Core.Step;

namespace Trax.Samples.EnergyHub.Trains.Microgrid.OptimizeMicrogrid.Steps;

public class ApplyDistributionStep(ILogger<ApplyDistributionStep> logger)
    : Step<OptimizeMicrogridInput, Unit>
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
