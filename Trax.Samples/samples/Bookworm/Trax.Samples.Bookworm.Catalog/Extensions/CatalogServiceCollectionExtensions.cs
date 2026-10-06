using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DomainContext;
using Trax.Samples.Bookworm.Catalog.Context;

namespace Trax.Samples.Bookworm.Catalog.Extensions;

/// <summary>Registration for the catalog data context.</summary>
public static class CatalogServiceCollectionExtensions
{
    /// <summary>Registers <see cref="CatalogDbContext"/> behind <see cref="ICatalogDbContext"/> against PostgreSQL.</summary>
    public static IServiceCollection AddCatalogDataContext(
        this IServiceCollection services,
        string connectionString
    ) =>
        services.AddDomainDataContext<ICatalogDbContext, CatalogDbContext>(options =>
            options.UseNpgsql(connectionString)
        );
}
