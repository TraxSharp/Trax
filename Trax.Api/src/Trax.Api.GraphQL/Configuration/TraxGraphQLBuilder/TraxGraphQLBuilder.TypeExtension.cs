using System.Reflection;
using HotChocolate.Types;

namespace Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;

public partial class TraxGraphQLBuilder
{
    /// <summary>
    /// Registers a single HotChocolate type extension class (typically decorated
    /// with <see cref="ExtendObjectTypeAttribute"/>) on the Trax GraphQL schema.
    /// </summary>
    /// <typeparam name="T">
    /// A class that extends a GraphQL type, usually annotated with
    /// <c>[ExtendObjectType]</c>.
    /// </typeparam>
    public TraxGraphQLBuilder AddTypeExtension<T>()
        where T : class
    {
        AdditionalTypeExtensions.Add(typeof(T));
        return this;
    }

    /// <summary>
    /// Scans the given assemblies for all non-abstract classes decorated with
    /// <see cref="ExtendObjectTypeAttribute"/> and registers them as type extensions
    /// on the Trax GraphQL schema.
    /// </summary>
    public TraxGraphQLBuilder AddTypeExtensions(params Assembly[] assemblies)
    {
        foreach (var assembly in assemblies)
        {
            var types = assembly
                .GetTypes()
                .Where(t =>
                    t is { IsAbstract: false, IsClass: true }
                    && t.GetCustomAttribute<ExtendObjectTypeAttribute>() != null
                );
            AdditionalTypeExtensions.AddRange(types);
        }

        return this;
    }
}
