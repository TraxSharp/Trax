using HotChocolate.Data.Filters;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.Filtering.ListElements;
using Trax.Api.GraphQL.TypeModules;
using Trax.Effect.Configuration.ClientExport;

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
/// The warning fires only for collections the built schema still exposes a filter on: the
/// filter input type of the model's entry field is the source of truth for what a caller
/// can filter. A registered <c>AddFilterType</c> override that removes the collection from
/// the input leaves no <c>some: { eq: }</c> filter for it to slow down, so the missing
/// index would slow nothing.
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
) : IHostedService, ITraxStartupCheck
{
    private const string NpgsqlProviderName = "Npgsql.EntityFrameworkCore.PostgreSQL";

    /// <summary>Schema name registered by Trax for the GraphQL endpoint.</summary>
    private const string TraxSchemaName = "trax";

    /// <summary>Annotation Npgsql writes for <c>HasMethod(...)</c> on an index.</summary>
    private const string IndexMethodAnnotation = "Npgsql:IndexMethod";

    private const string GinMethod = "gin";

    /// <summary>The argument the filtering convention adds to a query field.</summary>
    private const string FilterArgumentName = "where";

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var filterable = configuration
            .ModelRegistrations.Where(r => r.Attribute.Filtering)
            .ToList();

        if (filterable.Count == 0)
            return;

        using var scope = serviceProvider.CreateScope();

        var targets = new List<(DbContext Context, List<QueryModelRegistration> Registrations)>();
        foreach (var group in filterable.GroupBy(r => r.DbContextType))
        {
            if (scope.ServiceProvider.GetService(group.Key) is not DbContext dbContext)
                continue;

            // The operator choice is an Npgsql behaviour; other providers translate
            // membership their own way and have no GIN to miss.
            if (dbContext.Database.ProviderName != NpgsqlProviderName)
                continue;

            targets.Add((dbContext, group.ToList()));
        }

        if (targets.Count == 0)
            return;

        var schema = await ResolveSchemaAsync(scope.ServiceProvider, cancellationToken);
        if (schema is null)
            return;

        foreach (var (context, registrations) in targets)
        foreach (var registration in registrations)
            Inspect(context, registration, schema);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>
    /// Materialises the built schema so the warning can be checked against what a caller
    /// can actually filter. Returns null when the schema cannot be resolved: a host that
    /// cannot build its schema is broken for reasons other startup checks report, and this
    /// advisory must not be what throws.
    /// </summary>
    private static async Task<ISchemaDefinition?> ResolveSchemaAsync(
        IServiceProvider scopedProvider,
        CancellationToken cancellationToken
    )
    {
        try
        {
            if (scopedProvider.GetService<IRequestExecutorProvider>() is not { } executorProvider)
                return null;

            var executor = await executorProvider.GetExecutorAsync(
                TraxSchemaName,
                cancellationToken
            );
            return executor.Schema;
        }
        catch
        {
            return null;
        }
    }

    private void Inspect(
        DbContext dbContext,
        QueryModelRegistration registration,
        ISchemaDefinition schema
    )
    {
        var entityType = dbContext.Model.FindEntityType(registration.EntityType);
        if (entityType is null)
            return;

        var filterInput = FilterInputFor(schema, registration);
        if (filterInput is null)
            return;

        foreach (var property in entityType.GetProperties())
        {
            if (!ListElementFilterBinding.IsScalarCollection(property.ClrType))
                continue;

            if (!ExposedInFilterInput(filterInput, property))
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

    /// <summary>
    /// The filter input the built schema actually exposes for this model: the type of the
    /// <c>where</c> argument on the model's entry field under <c>discover</c>. Null when
    /// the entry field or the argument is absent — nothing is filterable then.
    /// </summary>
    private static IInputObjectTypeDefinition? FilterInputFor(
        ISchemaDefinition schema,
        QueryModelRegistration registration
    )
    {
        var discoverField = schema.QueryType.Fields.FirstOrDefault(f => f.Name == "discover");
        if (discoverField is null)
            return null;

        var container = (IObjectTypeDefinition)discoverField.Type.NamedType();

        if (registration.Attribute.Namespace is { } ns)
        {
            var nsField = container.Fields.FirstOrDefault(f =>
                f.Name == TrainTypeModule.CamelCase(ns)
            );
            if (nsField is null)
                return null;
            container = (IObjectTypeDefinition)nsField.Type.NamedType();
        }

        var entryField = container.Fields.FirstOrDefault(f =>
            f.Name == QueryModelTypeModule.FieldName(registration)
        );
        if (entryField is null)
            return null;

        var filterArg = entryField.Arguments.FirstOrDefault(a => a.Name == FilterArgumentName);
        if (filterArg is null)
            return null;

        return filterArg.Type.NamedType() as IInputObjectTypeDefinition;
    }

    /// <summary>
    /// True when the filter input still has a field bound to this property. Matched by
    /// the field's bound member, not its GraphQL name, so a field a
    /// <c>AddFilterType</c> override renamed still counts as exposed.
    /// </summary>
    private static bool ExposedInFilterInput(
        IInputObjectTypeDefinition filterInput,
        IProperty property
    )
    {
        var propertyName = property.Name;

        return filterInput
            .Fields.OfType<FilterField>()
            .Any(field => field.Member is { Name: var name } && name == propertyName);
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
