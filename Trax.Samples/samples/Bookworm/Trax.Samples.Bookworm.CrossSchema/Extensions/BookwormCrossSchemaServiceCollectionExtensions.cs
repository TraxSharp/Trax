using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.DataLoaders.CrossSchema;
using Trax.Samples.Bookworm.Catalog.Context;
using Trax.Samples.Bookworm.Catalog.Models.Books;

namespace Trax.Samples.Bookworm.CrossSchema.Extensions;

/// <summary>Registers the batched loaders behind Bookworm's cross-schema edges.</summary>
public static class BookwormCrossSchemaServiceCollectionExtensions
{
    /// <summary>
    /// Registers one loader per distinct (target context, target entity) pair referenced by
    /// <see cref="CrossSchemaEdges.All"/>. A meta-test fails if an edge has no registered loader.
    /// </summary>
    public static IServiceCollection AddBookwormCrossSchema(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddCrossSchemaLoader<CatalogDbContext, Book>();
        return services;
    }
}
