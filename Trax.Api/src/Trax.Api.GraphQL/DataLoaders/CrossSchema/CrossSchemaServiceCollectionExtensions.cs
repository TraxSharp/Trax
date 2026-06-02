using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.Api.GraphQL.DataLoaders.CrossSchema;

/// <summary>Registration for cross-schema data loaders.</summary>
public static class CrossSchemaServiceCollectionExtensions
{
    /// <summary>
    /// Registers the batched <see cref="CrossSchemaLoader{TContext, TEntity}"/> that backs every edge
    /// resolving <typeparamref name="TEntity"/> from <typeparamref name="TContext"/>. Call once per
    /// distinct (target context, target entity) pair the consumer's edge manifest references.
    /// </summary>
    public static IServiceCollection AddCrossSchemaLoader<TContext, TEntity>(
        this IServiceCollection services
    )
        where TContext : DbContext
        where TEntity : class
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddDataLoader<CrossSchemaLoader<TContext, TEntity>>();
        return services;
    }
}
