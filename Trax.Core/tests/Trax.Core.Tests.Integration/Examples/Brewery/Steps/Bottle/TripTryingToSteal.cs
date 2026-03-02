using Trax.Core.Exceptions;
using Trax.Core.Step;
using Trax.Core.Tests.Integration.Examples.Brewery.Steps.Prepare;

namespace Trax.Core.Tests.Integration.Examples.Brewery.Steps.Bottle;

public class TripTryingToSteal : Step<BrewingJug, List<GlassBottle>>
{
    public override async Task<List<GlassBottle>> Run(BrewingJug input)
    {
        // We try to steal the cinnamon, but we trip and fall and GO LEFT
        throw new TrainException("You done messed up now, son.");
    }
}
