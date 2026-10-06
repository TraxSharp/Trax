using Microsoft.Extensions.Logging;
using Trax.Core.Functional;
using Trax.Core.Junction;

namespace Trax.Samples.Scheduling.Trains.RepriceCatalog.Junctions;

public class ApplyRatesJunction(ILogger<ApplyRatesJunction> logger)
    : Junction<RepriceCatalogInput, Unit>
{
    public override Task<Unit> Run(RepriceCatalogInput input)
    {
        logger.LogInformation("Repriced the {Catalog} catalog at the new rates", input.Catalog);
        return Task.FromResult(Unit.Default);
    }
}
