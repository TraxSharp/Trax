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

                modelRegistrations.Add(
                    new QueryModelRegistration(
                        entityType,
                        dbContextType,
                        attr,
                        filterType,
                        sortType
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
