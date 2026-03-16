using LanguageExt;
using Trax.Core.Exceptions;
using Trax.Core.Junction;
using Trax.Core.Tests.Examples.Brewery.Junctions.Prepare;

namespace Trax.Core.Tests.Examples.Brewery.Junctions.Bottle;

public class StealCinnamonAndRunAway : Junction<BrewingJug, List<GlassBottle>>
{
    public override async Task<List<GlassBottle>> Run(BrewingJug input)
    {
        // We steal the Cinnamon Sticks and make a run for it with some empty bottles
        input.Ingredients.Cinnamon = 0;
        input.HasCinnamonSticks = false;
        var emptyBottles = new List<GlassBottle>()
        {
            new() { },
            new() { },
            new() { },
        };
        return emptyBottles;
    }
}
