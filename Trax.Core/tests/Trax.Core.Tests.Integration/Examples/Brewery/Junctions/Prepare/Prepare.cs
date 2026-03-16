using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Trax.Core.Exceptions;
using Trax.Core.Junction;
using Trax.Core.Tests.Integration.Examples.Brewery.Junctions.Ferment;

namespace Trax.Core.Tests.Integration.Examples.Brewery.Junctions.Prepare;

#pragma warning disable CS9113 // Parameter is unread - injected via DI for testing
public class Prepare(IFerment _ferment) : Junction<Ingredients, BrewingJug>, IPrepare
#pragma warning restore CS9113
{
    public override async Task<BrewingJug> Run(Ingredients input)
    {
        const int gallonWater = 1;

        var gallonAppleJuice = await Boil(gallonWater, input.Apples, input.BrownSugar);

        if (gallonAppleJuice.IsLeft)
            throw gallonAppleJuice.Swap().ValueUnsafe();

        return new BrewingJug() { Gallons = gallonAppleJuice.ValueUnsafe(), Ingredients = input };
    }

    private async Task<Either<TrainException, int>> Boil(
        int gallonWater,
        int numApples,
        int ozBrownSugar
    )
    {
        return gallonWater + (numApples / 8) + (ozBrownSugar / 128);
    }
}
