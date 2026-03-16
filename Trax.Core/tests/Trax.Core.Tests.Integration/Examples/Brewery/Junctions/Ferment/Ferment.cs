using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Trax.Core.Exceptions;
using Trax.Core.Junction;
using Trax.Core.Tests.Integration.Examples.Brewery.Junctions.Prepare;

namespace Trax.Core.Tests.Integration.Examples.Brewery.Junctions.Ferment;

public class Ferment : Junction<BrewingJug, Unit>, IFerment
{
    public override async Task<Unit> Run(BrewingJug input)
    {
        var cinnamonSticks = await AddCinnamonSticks(input);

        if (cinnamonSticks.IsLeft)
            throw cinnamonSticks.Swap().ValueUnsafe();

        var yeast = await AddYeast(input);

        if (yeast.IsLeft)
            throw yeast.Swap().ValueUnsafe();

        input.IsFermented = true;

        return Unit.Default;
    }

    public async Task<Either<TrainException, Unit>> AddCinnamonSticks(BrewingJug jug)
    {
        jug.HasCinnamonSticks = jug.Ingredients.Cinnamon > 0;

        return Unit.Default;
    }

    public async Task<Either<TrainException, Unit>> AddYeast(BrewingJug jug)
    {
        if (jug.Ingredients.Yeast <= 0)
            return new TrainException("We need yeast to make Cider!");

        jug.Yeast = jug.Ingredients.Yeast;

        return Unit.Default;
    }
}
