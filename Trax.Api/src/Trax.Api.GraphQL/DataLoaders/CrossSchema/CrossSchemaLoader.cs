using GreenDonut;
using Microsoft.EntityFrameworkCore;

namespace Trax.Api.GraphQL.DataLoaders.CrossSchema;

/// <summary>
/// Batches the resolution of an entity that lives in a <b>different</b> schema/context than the
/// parent object being resolved. This is the sanctioned way to follow a relationship across a
/// one-project-one-schema-one-context boundary in GraphQL.
/// </summary>
/// <typeparam name="TContext">The context that owns <typeparamref name="TEntity"/>.</typeparam>
/// <typeparam name="TEntity">The target entity. Must have an integer <c>Id</c> primary key.</typeparam>
/// <remarks>
/// EF Core cannot JOIN across two <see cref="DbContext"/> instances, so a cross-context relationship
/// has no SQL join available and would otherwise resolve one row at a time (N+1). This loader collects
/// every requested key across the whole GraphQL request and issues a single <c>WHERE Id IN (...)</c>
/// against the owning context.
/// <para>
/// A <see cref="DbContext"/> is not thread-safe and a loader batch may run concurrently with other
/// resolvers, so the loader creates its own short-lived context from the pooled factory per batch
/// rather than sharing the request-scoped instance.
/// </para>
/// </remarks>
public sealed class CrossSchemaLoader<TContext, TEntity>(
    IDbContextFactory<TContext> contextFactory,
    IBatchScheduler batchScheduler,
    DataLoaderOptions options
) : BatchDataLoader<int, TEntity>(batchScheduler, options)
    where TContext : DbContext
    where TEntity : class
{
    protected override async Task<IReadOnlyDictionary<int, TEntity>> LoadBatchAsync(
        IReadOnlyList<int> keys,
        CancellationToken cancellationToken
    )
    {
        await using var db = await contextFactory.CreateDbContextAsync(cancellationToken);

        // EF.Property translates inside the SQL WHERE, but cannot be used as a client-side key
        // selector, so the rows are materialized and then keyed by reflecting the Id.
        var rows = await db.Set<TEntity>()
            .Where(e => keys.Contains(EF.Property<int>(e, "Id")))
            .ToListAsync(cancellationToken);

        var idProperty =
            typeof(TEntity).GetProperty("Id")
            ?? throw new InvalidOperationException(
                $"{typeof(TEntity).Name} has no Id property; CrossSchemaLoader requires an integer "
                    + "Id primary key on the target entity."
            );

        return rows.ToDictionary(row => (int)idProperty.GetValue(row)!);
    }
}
