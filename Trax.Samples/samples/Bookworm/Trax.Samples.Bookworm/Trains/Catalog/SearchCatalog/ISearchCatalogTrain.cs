using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.Bookworm.Trains.Catalog.SearchCatalog;

public interface ISearchCatalogTrain : IServiceTrain<SearchCatalogInput, SearchCatalogOutput>;
