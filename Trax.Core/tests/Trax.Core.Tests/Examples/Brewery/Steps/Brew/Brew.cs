using LanguageExt;
using Trax.Core.Exceptions;
using Trax.Core.Step;
using Trax.Core.Tests.Examples.Brewery.Steps.Prepare;

namespace Trax.Core.Tests.Examples.Brewery.Steps.Brew;

public class Brew : Step<BrewingJug, Unit>, IBrew
{
    public override async Task<Unit> Run(BrewingJug input)
    {
        if (!input.IsFermented)
            throw new WorkflowException("We cannot brew our Cider before it is fermented!");

        // Pretend that we waited 2 days...
        input.IsBrewed = true;

        return Unit.Default;
    }
}
