using LanguageExt;
using Trax.Core.Exceptions;
using Trax.Core.Junction;
using Trax.Core.Tests.Examples.Brewery.Junctions.Ferment;
using Trax.Core.Tests.Examples.Brewery.Junctions.Prepare;

namespace Trax.Core.Tests.Examples.Brewery.Junctions.Bottle;

#pragma warning disable CS9113 // Parameter is unread - injected via DI for testing
public class Bottle(IFerment _ferment) : Junction<BrewingJug, List<GlassBottle>>, IBottle
#pragma warning restore CS9113
{
    public override async Task<List<GlassBottle>> Run(BrewingJug input)
    {
        if (!input.IsBrewed)
            throw new TrainException(
                "We don't want to bottle un-brewed beer! What are we, trying to make poison?"
            );

        // 16 oz bottles
        var bottlesNeeded = input.Gallons / 8;

        var filledBottles = new List<GlassBottle>();
        for (var i = 0; i < bottlesNeeded; i++)
        {
            filledBottles.Add(new GlassBottle() { HasCider = true });
        }

        return filledBottles;
    }
}
