using LanguageExt;
using Trax.Core.Exceptions;
using Trax.Core.Tests.Examples.Brewery.Junctions.Bottle;
using Trax.Core.Tests.Examples.Brewery.Junctions.Brew;
using Trax.Core.Tests.Examples.Brewery.Junctions.Ferment;
using Trax.Core.Tests.Examples.Brewery.Junctions.Prepare;
using Trax.Core.Train;

namespace Trax.Core.Tests.Examples.Brewery;

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
