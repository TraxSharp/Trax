using System.ComponentModel.DataAnnotations.Schema;
using System.Linq.Expressions;
using System.Reflection;
using HotChocolate.Authorization;
using HotChocolate.Data;
using HotChocolate.Data.Filters;
using HotChocolate.Data.Sorting;
using HotChocolate.Execution.Configuration;
using HotChocolate.Language;
using HotChocolate.Types;
using HotChocolate.Types.Descriptors;
using HotChocolate.Types.Pagination;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.GraphQL.Authorization;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.Projection;
using Trax.Api.GraphQL.Queries;
using Trax.Effect.Attributes;

namespace Trax.Api.GraphQL.TypeModules;

/// <summary>
/// A HotChocolate TypeModule that dynamically generates GraphQL query fields
/// for entities marked with <c>[TraxQueryModel]</c>. Each entity gets a query
/// field under <c>discover</c> with optional cursor pagination, filtering,
/// sorting, and projection based on the attribute configuration.
/// </summary>
internal sealed class QueryModelTypeModule(GraphQLConfiguration configuration) : TypeModule
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
                        .Invoke(
                            null,
                            [reg.Attribute, reg.AuthorizeAttributes, reg.AllowAnonymous]
                        )!;
                types.Add(objectType);
            }
        }

        // Group model registrations by namespace. A namespace that holds any query model is
        // owned by this module: it declares the namespace type and its field on DiscoverQueries,
        // and TrainTypeModule only extends the type with its train fields.
        var byNamespace = registrations.GroupBy(r =>
            r.Attribute.Namespace is { } ns ? TrainTypeModule.CamelCase(ns) : null
        );

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
                continue;
            }

            var nsTypeName = TrainTypeModule.NamespaceTypeName(group.Key, "DiscoverQueries");

            types.Add(NamespaceTypes.Base(nsTypeName));
            types.Add(
                new ObjectTypeExtension(d =>
                {
                    d.Name(nsTypeName);
                    foreach (var reg in group)
                        AddModelQueryField(d, reg);
                })
            );
            types.Add(NamespaceTypes.Field("DiscoverQueries", group.Key, nsTypeName));
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
        var field = descriptor.Field(FieldName(reg));

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

        // Apply [TraxAuthorize] at field level so the entry point itself is
        // gated. Type-level @authorize alone leaves a hole: a request that
        // selects only Connection-shaped scalars like `totalCount` or
        // `pageInfo.hasNextPage` never resolves a node of the entity type,
        // so the type-level directive does not fire. Field-level enforcement
        // blocks the entry point unconditionally; type-level enforcement
        // (in CreateObjectType) covers the type wherever a navigation from an
        // ungated parent selects it. A navigation used only inside where or
        // order selects nothing, so NavigationInputAuthorization (below)
        // evaluates the same directives for it.
        //
        // [TraxAllowAnonymous] short-circuits both gates: the entity is
        // explicitly anonymous-readable, so emitting @authorize on its entry
        // field would defeat the opt-in. The mutual-exclusion guard at
        // TraxGraphQLBuilder.Build() guarantees AllowAnonymous and any
        // [TraxAuthorize] attribute cannot both be set, so this check is
        // mutually exclusive with the directive emission below.
        if (!reg.AllowAnonymous)
            AuthorizeDirectives.Apply(field, reg.AuthorizeAttributes);

        // A gated type reached through a navigation in the caller's where or order is
        // authorized as if it were selected. Registered first so it runs before paging,
        // filtering and sorting touch the database.
        if (attr.Filtering || attr.Sorting)
            NavigationInputAuthorization.Apply(field, typeof(TEntity));

        // Apply features in the correct middleware pipeline order:
        // Paging > Projection > Filtering > Sorting
        if (attr.Paging)
        {
            field.UsePaging<ObjectType<TEntity>>(
                options: new PagingOptions { IncludeTotalCount = true }
            );
        }

        if (attr.Projection)
            field.Use(QueryModelProjection.CreateMiddleware<TEntity>());

        // Filtering and sorting follow the exposed field set: the input types are bound to the
        // entity in the conventions (see GraphQLServiceExtensions), so a model's own entry field
        // and every navigation reaching it from another model offer the same fields its object
        // type does.
        if (attr.Filtering)
        {
            if (reg.FilterInputType is not null)
                field.UseFiltering(reg.FilterInputType);
            else
                field.UseFiltering<QueryModelFilterInputType<TEntity>>();
        }

        if (attr.Sorting)
        {
            if (reg.SortInputType is not null)
                field.UseSorting(reg.SortInputType);
            else
                field.UseSorting<QueryModelSortInputType<TEntity>>();
        }

        field.Resolve(ctx =>
        {
            var dbContext = (DbContext)ctx.Services.GetRequiredService(reg.DbContextType);
            return dbContext.Set<TEntity>();
        });
    }

    private static ObjectType<TEntity> CreateObjectType<TEntity>(
        TraxQueryModelAttribute attr,
        IReadOnlyList<TraxAuthorizeAttribute> authorizeAttributes,
        bool allowAnonymous
    )
        where TEntity : class
    {
        // ExposeAs takes precedence: build the GraphQL type from the supplied
        // interface's property set, not the entity's full public surface.
        // The combination ExposeAs + BindFields.Explicit is rejected at build
        // time (see TraxGraphQLBuilder.Build), so we don't need to consider it here.
        //
        // [TraxAllowAnonymous] short-circuits @authorize emission in all three
        // branches below. The build-time mutual-exclusion guard guarantees
        // AllowAnonymous and [TraxAuthorize] cannot both be set, but the
        // explicit `!allowAnonymous` checks make the intent visible in code
        // so a future refactor of AuthorizeDirectives.Apply cannot accidentally
        // re-emit the directive on an anonymous entity.
        if (attr.ExposeAs is { } exposeAs)
        {
            var allowedNames = GetExposedPropertyNames(exposeAs);

            return new ObjectType<TEntity>(descriptor =>
            {
                descriptor.BindFieldsExplicitly();

                foreach (
                    var prop in typeof(TEntity).GetProperties(
                        BindingFlags.Public | BindingFlags.Instance
                    )
                )
                {
                    // Property names on the entity that also appear on the
                    // exposed interface get bound; everything else is hidden.
                    // Explicit interface implementations don't have matching
                    // public properties on the entity and are silently skipped
                    // (the build-time validator surfaces this as an error).
                    if (allowedNames.Contains(prop.Name))
                        descriptor.Field(prop);
                }

                if (!allowAnonymous)
                    AuthorizeDirectives.Apply(descriptor, authorizeAttributes);
            });
        }

        if (attr.BindFields != FieldBindingBehavior.Explicit)
        {
            if (allowAnonymous || authorizeAttributes.Count == 0)
                return new ObjectType<TEntity>();

            return new ObjectType<TEntity>(descriptor =>
                AuthorizeDirectives.Apply(descriptor, authorizeAttributes)
            );
        }

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

            if (!allowAnonymous)
                AuthorizeDirectives.Apply(descriptor, authorizeAttributes);
        });
    }

    /// <summary>
    /// Returns the set of property names declared on <paramref name="exposeAs"/>
    /// and every interface it inherits from (transitively).
    /// <see cref="Type.GetProperties()"/> on an interface only returns members
    /// declared directly on that interface, so we walk <see cref="Type.GetInterfaces"/>
    /// to flatten the hierarchy.
    /// </summary>
    internal static HashSet<string> GetExposedPropertyNames(Type exposeAs)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        void Collect(Type iface)
        {
            foreach (var prop in iface.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                names.Add(prop.Name);
            }

            foreach (var parent in iface.GetInterfaces())
                Collect(parent);
        }

        Collect(exposeAs);
        return names;
    }

    /// <summary>The field a query model's entry takes under <c>discover</c> or its namespace.</summary>
    internal static string FieldName(QueryModelRegistration reg) =>
        reg.Attribute.Name ?? DeriveModelName(reg.EntityType.Name);

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
