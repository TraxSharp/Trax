using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Bookworm.Trains.Catalog.SearchCatalog.Junctions;

namespace Trax.Samples.Bookworm.Trains.Catalog.SearchCatalog;

/// <summary>Searches the catalog by title. A read operation, exposed as a GraphQL query.</summary>
[TraxAllowAnonymous]
[TraxQuery(Namespace = GraphQLNamespaces.Catalog, Description = "Searches the catalog by title")]
public class SearchCatalogTrain
    : ServiceTrain<SearchCatalogInput, SearchCatalogOutput>,
        ISearchCatalogTrain
{
    protected override Task<Either<Exception, SearchCatalogOutput>> Junctions() =>
        Chain<SearchCatalogJunction>().Resolve();
}
