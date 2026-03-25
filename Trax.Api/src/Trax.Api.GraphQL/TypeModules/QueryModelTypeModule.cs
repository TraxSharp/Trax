using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;
using HotChocolate.Data;
using HotChocolate.Execution.Configuration;
using HotChocolate.Language;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Pagination;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.Queries;
using Trax.Effect.Attributes;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>
/// A HotChocolate TypeModule that dynamically generates GraphQL query fields
/// for entities marked with <c>[TraxQueryModel]</c>. Each entity gets a query
/// field under <c>discover</c> with optional cursor pagination, filtering,
/// sorting, and projection based on the attribute configuration.
/// </summary>
public class QueryModelTypeModule(GraphQLConfiguration configuration) : TypeModule
{
    /// <summary>
    /// Discovers all registered query model entities and generates the GraphQL schema types:
    /// - ObjectType for each unique entity type
    /// - ObjectTypeExtension on "DiscoverQueries" to add query fields
    /// </summary>
    public override ValueTask<IReadOnlyCollection<ITypeSystemMember>> CreateTypesAsync(
        IDescriptorContext context,
        CancellationToken cancellationToken
    )
    {
        var types = new List<ITypeSystemMember>();
        var registrations = configuration.ModelRegistrations;

        if (registrations.Count == 0)
            return new(types);

        var usedEntityTypes = new HashSet<Type>();
        foreach (var reg in registrations)
        {
            if (usedEntityTypes.Add(reg.EntityType))
            {
                var objectType = (ITypeSystemMember)
                    CreateObjectTypeMethod
                        .MakeGenericMethod(reg.EntityType)
                        .Invoke(null, [reg.Attribute])!;
                types.Add(objectType);
            }
        }

        // Group model registrations by namespace
        var byNamespace = registrations.GroupBy(r => r.Attribute.Namespace);

        foreach (var group in byNamespace)
        {
            if (group.Key is null)
            {
                // No namespace — add fields directly to DiscoverQueries
                types.Add(
                    new ObjectTypeExtension(d =>
                    {
                        d.Name("DiscoverQueries");
                        foreach (var reg in group)
                            AddModelQueryField(d, reg);
                    })
                );
            }
            else
            {
                // Namespace — create/extend intermediate type
                var nsTypeName = TrainTypeModule.NamespaceTypeName(group.Key, "DiscoverQueries");
                var nsFieldName = TrainTypeModule.CamelCase(group.Key);

                // Register the base ObjectType for this namespace (only once across modules)
                if (configuration.RegisteredNamespaceTypes.Add(nsTypeName))
                {
                    types.Add(new ObjectType(d => d.Name(nsTypeName)));
                }

                // Add fields to the namespace type
                types.Add(
                    new ObjectTypeExtension(d =>
                    {
                        d.Name(nsTypeName);
                        foreach (var reg in group)
                            AddModelQueryField(d, reg);
                    })
                );

                // Add the namespace field to DiscoverQueries (only once across modules)
                var nsFieldKey = $"DiscoverQueries.{nsFieldName}";
                if (configuration.RegisteredNamespaceTypes.Add(nsFieldKey))
                {
                    var capturedNsTypeName = nsTypeName;
                    types.Add(
                        new ObjectTypeExtension(d =>
                        {
                            d.Name("DiscoverQueries");
                            d.Field(nsFieldName)
                                .Type(new NamedTypeNode(capturedNsTypeName))
                                .Resolve(_ => new object());
                        })
                    );
                }
            }
        }

        return new(types);
    }

    private static readonly MethodInfo CreateObjectTypeMethod =
        typeof(QueryModelTypeModule).GetMethod(
            nameof(CreateObjectType),
            BindingFlags.NonPublic | BindingFlags.Static
        )!;

    private static readonly MethodInfo ConfigureFieldMethod =
        typeof(QueryModelTypeModule).GetMethod(
            nameof(ConfigureField),
            BindingFlags.NonPublic | BindingFlags.Static
        )!;

    private static void AddModelQueryField(
        IObjectTypeDescriptor descriptor,
        QueryModelRegistration reg
    )
    {
        var fieldName = reg.Attribute.Name ?? DeriveModelName(reg.EntityType.Name);

        var field = descriptor.Field(fieldName);

        if (reg.Attribute.Description is not null)
            field.Description(reg.Attribute.Description);

        if (reg.Attribute.DeprecationReason is not null)
            field.Deprecated(reg.Attribute.DeprecationReason);

        // Delegate to a generic method so HotChocolate gets properly typed
        // delegates for projection, filtering, sorting, and the resolver.
        ConfigureFieldMethod.MakeGenericMethod(reg.EntityType).Invoke(null, [field, reg]);
    }

    private static void ConfigureField<TEntity>(
        IObjectFieldDescriptor field,
        QueryModelRegistration reg
    )
        where TEntity : class
    {
        var attr = reg.Attribute;

        // Apply features in the correct middleware pipeline order:
        // Paging > Projection > Filtering > Sorting
        if (attr.Paging)
        {
            field.UsePaging<ObjectType<TEntity>>(
                options: new PagingOptions { IncludeTotalCount = true }
            );
        }

        if (attr.Projection)
            field.UseProjection<TEntity>();

        if (attr.Filtering)
        {
            if (reg.FilterInputType is not null)
                field.UseFiltering(reg.FilterInputType);
            else
                field.UseFiltering<TEntity>();
        }

        if (attr.Sorting)
        {
            if (reg.SortInputType is not null)
                field.UseSorting(reg.SortInputType);
            else
                field.UseSorting<TEntity>();
        }

        field.Resolve(ctx =>
        {
            var dbContext = (DbContext)ctx.Services.GetRequiredService(reg.DbContextType);
            return dbContext.Set<TEntity>();
        });
    }

    private static ObjectType<TEntity> CreateObjectType<TEntity>(TraxQueryModelAttribute attr)
        where TEntity : class
    {
        if (attr.BindFields != FieldBindingBehavior.Explicit)
            return new ObjectType<TEntity>();

        return new ObjectType<TEntity>(descriptor =>
        {
            descriptor.BindFieldsExplicitly();

            foreach (
                var prop in typeof(TEntity).GetProperties(
                    BindingFlags.Public | BindingFlags.Instance
                )
            )
            {
                if (prop.GetCustomAttribute<ColumnAttribute>() is not null)
                    descriptor.Field(prop);
            }
        });
    }

    /// <summary>
    /// Derives a pluralized camelCase GraphQL field name from a class name.
    /// e.g. "Player" → "players", "Match" → "matches", "Category" → "categories"
    /// </summary>
    internal static string DeriveModelName(string typeName)
    {
        var plural = Pluralize(typeName);
        return char.ToLowerInvariant(plural[0]) + plural[1..];
    }

    internal static string Pluralize(string name)
    {
        if (
            name.EndsWith("s", StringComparison.Ordinal)
            || name.EndsWith("x", StringComparison.Ordinal)
            || name.EndsWith("z", StringComparison.Ordinal)
            || name.EndsWith("ch", StringComparison.Ordinal)
            || name.EndsWith("sh", StringComparison.Ordinal)
        )
            return name + "es";

        if (name.EndsWith("y", StringComparison.Ordinal) && name.Length > 1 && !IsVowel(name[^2]))
            return name[..^1] + "ies";

        return name + "s";
    }

    private static bool IsVowel(char c) => "aeiouAEIOU".Contains(c);
}
