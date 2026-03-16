using LanguageExt;
using Trax.Core.Exceptions;
using Trax.Core.Junction;
using Trax.Core.Tests.Examples.Brewery.Junctions.Prepare;

namespace Trax.Core.Tests.Examples.Brewery.Junctions.Brew;

public class Brew : Junction<BrewingJug, Unit>, IBrew
{
    public override async Task<Unit> Run(BrewingJug input)
    {
        if (!input.IsFermented)
            throw new TrainException("We cannot brew our Cider before it is fermented!");

        // Pretend that we waited 2 days...
        input.IsBrewed = true;

        return Unit.Default;
    }
}
