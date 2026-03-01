using LanguageExt;
using Trax.Core.Tests.Integration.Examples.Brewery.Steps.Bottle;
using Trax.Core.Tests.Integration.Examples.Brewery.Steps.Brew;
using Trax.Core.Tests.Integration.Examples.Brewery.Steps.Ferment;
using Trax.Core.Tests.Integration.Examples.Brewery.Steps.Prepare;
using Trax.Core.Train;

namespace Trax.Core.Tests.Integration.Examples.Brewery;

public class Cider(IPrepare prepare, IFerment ferment, IBrew brew, IBottle bottle)
    : Train<Ingredients, List<GlassBottle>>,
        ICider
{
    protected override async Task<Either<Exception, List<GlassBottle>>> RunInternal(
        Ingredients input
    ) =>
        Activate(input)
            .AddServices<IPrepare, IFerment, IBrew, IBottle>(prepare, ferment, brew, bottle)
            .IChain<IPrepare>()
            .IChain<IFerment>()
            .IChain<IBrew>()
            .IChain<IBottle>()
            .Resolve();
}
