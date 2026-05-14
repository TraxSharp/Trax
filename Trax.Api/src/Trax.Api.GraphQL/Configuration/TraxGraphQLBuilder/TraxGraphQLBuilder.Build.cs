using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Attributes;

namespace Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;

public partial class TraxGraphQLBuilder
{
    internal GraphQLConfiguration Build()
    {
        var modelRegistrations = new List<QueryModelRegistration>();

        foreach (var dbContextType in DbContextTypes)
        {
            var dbSetProps = dbContextType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p =>
                    p.PropertyType.IsGenericType
                    && p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>)
                );

            foreach (var prop in dbSetProps)
            {
                var entityType = prop.PropertyType.GetGenericArguments()[0];
                var attr = entityType.GetCustomAttribute<TraxQueryModelAttribute>();
                if (attr is null)
                    continue;

                ValidateExposeAs(entityType, attr);

                FilterTypeOverrides.TryGetValue(entityType, out var filterType);
                SortTypeOverrides.TryGetValue(entityType, out var sortType);

                var authorizeAttributes = DiscoverAuthorizeAttributes(entityType);
                ValidateAuthorizeAttributeShapes(entityType, authorizeAttributes);

                modelRegistrations.Add(
                    new QueryModelRegistration(
                        entityType,
                        dbContextType,
                        attr,
                        filterType,
                        sortType,
                        authorizeAttributes
                    )
                );
            }
        }

        return new GraphQLConfiguration(
            modelRegistrations,
            AdditionalTypeModules,
            SchemaConfigurations,
            AdditionalTypeExtensions,
            MaxExecutionDepthValue,
            CostOverride,
            IntrospectionPredicate,
            MaxOperationsPerRequestValue,
            AuthorizationRequired,
            AuthorizationPolicy,
            OperationQueriesExposed,
            OperationMutationsExposed
        );
    }

    /// <summary>
    /// Collects every <see cref="TraxAuthorizeAttribute"/> declared on the entity type
    /// (including any inherited via base classes or interfaces). De-duplicates by
    /// reference identity so a single attribute instance is not counted twice when the
    /// CLR returns it via multiple inheritance paths.
    /// </summary>
    private static IReadOnlyList<TraxAuthorizeAttribute> DiscoverAuthorizeAttributes(
        Type entityType
    )
    {
        var seen = new HashSet<TraxAuthorizeAttribute>(ReferenceEqualityComparer.Instance);
        var ordered = new List<TraxAuthorizeAttribute>();

        // The entity class itself, plus every interface it implements. `Inherited = true`
        // on the attribute already walks the base-class chain.
        var carriers = new List<Type> { entityType };
        carriers.AddRange(entityType.GetInterfaces());

        foreach (var carrier in carriers)
        {
            foreach (var attr in carrier.GetCustomAttributes<TraxAuthorizeAttribute>(inherit: true))
            {
                if (seen.Add(attr))
                    ordered.Add(attr);
            }
        }

        return ordered;
    }

    /// <summary>
    /// Build-time shape validation for <see cref="TraxAuthorizeAttribute"/> on a query
    /// model entity. Mirrors the train-side validator at
    /// <c>AuthorizationRegistrationValidator.ValidateAttributeShapes</c>: whitespace
    /// Policy and Roles values are caught here rather than producing a runtime gate
    /// that silently denies everyone.
    /// </summary>
    private static void ValidateAuthorizeAttributeShapes(
        Type entityType,
        IReadOnlyList<TraxAuthorizeAttribute> attributes
    )
    {
        foreach (var attribute in attributes)
        {
            if (attribute.Policy is not null && string.IsNullOrWhiteSpace(attribute.Policy))
                throw new InvalidOperationException(
                    $"[TraxAuthorize] on '{entityType.FullName}' has an empty or whitespace "
                        + "Policy value. Remove the parameter or provide a real policy name."
                );

            if (
                attribute.Roles is not null
                && attribute
                    .Roles.Split(',', StringSplitOptions.TrimEntries)
                    .All(string.IsNullOrEmpty)
            )
                throw new InvalidOperationException(
                    $"[TraxAuthorize(Roles=\"{attribute.Roles}\")] on '{entityType.FullName}' "
                        + "parsed to zero roles after splitting on ','. Remove the Roles "
                        + "argument or provide one or more non-empty role names."
                );
        }
    }

    private static void ValidateExposeAs(Type entityType, TraxQueryModelAttribute attr)
    {
        if (attr.ExposeAs is not { } exposeAs)
            return;

        if (attr.BindFields == FieldBindingBehavior.Explicit)
        {
            throw new InvalidOperationException(
                $"[TraxQueryModel] on {entityType.Name}: cannot set both "
                    + $"BindFields = Explicit and ExposeAs = typeof({exposeAs.Name}). "
                    + "Both restrict the exposed field set; choose one. Drop "
                    + "BindFields if ExposeAs is the source of truth, or drop "
                    + "ExposeAs if [Column]-decorated properties are."
            );
        }

        if (!exposeAs.IsInterface)
        {
            throw new InvalidOperationException(
                $"[TraxQueryModel(ExposeAs = typeof({exposeAs.Name}))] on "
                    + $"{entityType.Name}: ExposeAs must reference an interface, "
                    + $"but {exposeAs.Name} is a {(exposeAs.IsClass ? "class" : "non-interface type")}. "
                    + "Create a scalar-only interface (e.g. I"
                    + entityType.Name
                    + "Reference) and have the entity implement it."
            );
        }

        if (!exposeAs.IsAssignableFrom(entityType))
        {
            throw new InvalidOperationException(
                $"[TraxQueryModel(ExposeAs = typeof({exposeAs.Name}))] on "
                    + $"{entityType.Name}: the entity does not implement "
                    + $"{exposeAs.Name}. Add `: {exposeAs.Name}` to {entityType.Name} "
                    + "or remove ExposeAs."
            );
        }

        // The exposed interface must have at least one property; an empty
        // interface produces a GraphQL type with no fields, which is invalid
        // and would only emerge as a confusing HC error at schema build time.
        var exposedNames = TypeModules.QueryModelTypeModule.GetExposedPropertyNames(exposeAs);
        if (exposedNames.Count == 0)
        {
            throw new InvalidOperationException(
                $"[TraxQueryModel(ExposeAs = typeof({exposeAs.Name}))] on "
                    + $"{entityType.Name}: {exposeAs.Name} declares no properties. "
                    + "GraphQL types require at least one field. Add scalar properties "
                    + "to the interface or remove ExposeAs."
            );
        }

        // Catch the explicit-interface-implementation footgun: if the interface
        // has a property that the entity implements explicitly (or hides via
        // `new`), reflection's name match won't find a matching public
        // property on the entity and the field will silently vanish. Fail loud.
        foreach (var name in exposedNames)
        {
            var entityProp = entityType.GetProperty(
                name,
                BindingFlags.Public | BindingFlags.Instance
            );

            if (entityProp is null)
            {
                throw new InvalidOperationException(
                    $"[TraxQueryModel(ExposeAs = typeof({exposeAs.Name}))] on "
                        + $"{entityType.Name}: the interface declares '{name}', but "
                        + $"{entityType.Name} has no matching public instance "
                        + "property. ExposeAs requires implicit interface "
                        + "implementations so the GraphQL field can be named. "
                        + "Replace the explicit implementation with an implicit "
                        + "one, or remove the member from the interface."
                );
            }
        }
    }
}
