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
            AdditionalTypeExtensions
        );
    }
}
