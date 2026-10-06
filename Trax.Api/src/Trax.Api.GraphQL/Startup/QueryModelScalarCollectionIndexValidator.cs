using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.Filtering.ListElements;

namespace Trax.Api.GraphQL.Startup;

/// <summary>
/// Warns at host start when a filterable scalar collection on a query model has no GIN
/// index declared in the EF Core model.
/// </summary>
/// <remarks>
/// <para>
/// The declaration is what selects the operator Npgsql emits, not just what creates the
/// index. For a single-value membership filter (<c>some: { eq: X }</c>) Npgsql compiles:
/// </para>
/// <code>
/// HasIndex(x => x.Badges).HasMethod("gin")   ->  "badges" @> ARRAY[@p]   -- GIN can serve this
/// no index declared                          ->  @p = ANY ("badges")     -- nothing can
/// </code>
/// <para>
/// Both are correct SQL and return identical rows, and the GraphQL schema, the query and
/// the response are byte-for-byte the same either way. The only difference is the query
/// plan, which is why this is worth saying out loud at startup rather than leaving to be
/// discovered under load. The multi-value operators (<c>some: { in: }</c> to <c>&amp;&amp;</c>,
/// <c>all: { in: }</c> to <c>&lt;@</c>) are emitted either way and are unaffected.
/// </para>
/// <para>
/// Advisory only: it logs and never blocks startup. A collection that is small, rarely
/// filtered, or filtered only with the multi-value operators does not need the index.
/// </para>
/// </remarks>
internal sealed class QueryModelScalarCollectionIndexValidator(
    GraphQLConfiguration configuration,
    IServiceProvider serviceProvider,
    ILogger<QueryModelScalarCollectionIndexValidator> logger
) : IHostedService
{
    private const string NpgsqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";

    /// <summary>Annotation Npgsql writes for <c>HasMethod(...)</c> on an index.</summary>
    private const string IndexMethodAnnotation = "Npgsql:IndexMethod";

    private const string GinMethod = "gin";

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var filterable = configuration
            .ModelRegistrations.Where(r => r.Attribute.Filtering)
            .ToList();

        if (filterable.Count == 0)
            return Task.CompletedTask;

        using var scope = serviceProvider.CreateScope();

        foreach (var group in filterable.GroupBy(r => r.DbContextType))
        {
            if (scope.ServiceProvider.GetService(group.Key) is not DbContext dbContext)
                continue;

            // The operator choice is an Npgsql behaviour; other providers translate
            // membership their own way and have no GIN to miss.
            if (dbContext.Database.ProviderName != NpgsqlProviderName)
                continue;

            foreach (var registration in group)
                Inspect(dbContext, registration);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private void Inspect(DbContext dbContext, QueryModelRegistration registration)
    {
        var entityType = dbContext.Model.FindEntityType(registration.EntityType);
        if (entityType is null)
            return;

        foreach (var property in entityType.GetProperties())
        {
            if (!ListElementFilterBinding.IsScalarCollection(property.ClrType))
                continue;

            if (HasGinIndex(entityType, property))
                continue;

            logger.LogWarning(
                "Query model {Entity} exposes filtering on scalar collection {Property} ({ClrType}) "
                    + "with no GIN index declared in the EF model. Npgsql will compile "
                    + "`{Field}: {{ some: {{ eq: ... }} }}` to `= ANY(...)`, which no index can serve. "
                    + "Add HasIndex(x => x.{Property}).HasMethod(\"gin\") and a matching migration "
                    + "to get `@>` instead.",
                registration.EntityType.Name,
                property.Name,
                property.ClrType.Name,
                property.Name,
                property.Name
            );
        }
    }

    private static bool HasGinIndex(IEntityType entityType, IProperty property) =>
        entityType
            .GetIndexes()
            .Any(index =>
                index.Properties.Contains(property)
                && string.Equals(
                    index.FindAnnotation(IndexMethodAnnotation)?.Value as string,
                    GinMethod,
                    StringComparison.OrdinalIgnoreCase
                )
            );
}
