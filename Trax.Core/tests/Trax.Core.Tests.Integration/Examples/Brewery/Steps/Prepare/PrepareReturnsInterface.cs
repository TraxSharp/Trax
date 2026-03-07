using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Trax.Core.Exceptions;
using Trax.Core.Step;
using Trax.Core.Tests.Integration.Examples.Brewery.Steps.Ferment;

namespace Trax.Core.Tests.Integration.Examples.Brewery.Steps.Prepare;

#pragma warning disable CS9113 // Parameter is unread - injected via DI for testing
public class PrepareWithInterface(IFerment _ferment) : Step<Ingredients, IBrewingJug>
#pragma warning restore CS9113
{
    public override async Task<IBrewingJug> Run(Ingredients input)
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
