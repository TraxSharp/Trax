using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Trax.Core.Exceptions;
using Trax.Core.Step;
using Trax.Core.Tests.Integration.Examples.Brewery.Steps.Ferment;

namespace Trax.Core.Tests.Integration.Examples.Brewery.Steps.Prepare;

public class Prepare(IFerment _ferment) : Step<Ingredients, BrewingJug>, IPrepare
{
    public override async Task<BrewingJug> Run(Ingredients input)
    {
        const int gallonWater = 1;

        var gallonAppleJuice = await Boil(gallonWater, input.Apples, input.BrownSugar);

        if (gallonAppleJuice.IsLeft)
            throw gallonAppleJuice.Swap().ValueUnsafe();

        return new BrewingJug() { Gallons = gallonAppleJuice.ValueUnsafe(), Ingredients = input };
    }

    private async Task<Either<WorkflowException, int>> Boil(
        int gallonWater,
        int numApples,
        int ozBrownSugar
    )
    {
        return gallonWater + (numApples / 8) + (ozBrownSugar / 128);
    }
}
