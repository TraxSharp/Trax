using LanguageExt;
using Trax.Core.Exceptions;
using Trax.Core.Junction;
using Trax.Core.Tests.Examples.Brewery.Junctions.Prepare;

namespace Trax.Core.Tests.Examples.Brewery.Junctions.Bottle;

public class TripTryingToSteal : Junction<BrewingJug, List<GlassBottle>>
{
    public override async Task<List<GlassBottle>> Run(BrewingJug input)
    {
        // We try to steal the cinnamon, but we trip and fall and GO LEFT
        throw new TrainException("You done messed up now, son.");
    }
}
