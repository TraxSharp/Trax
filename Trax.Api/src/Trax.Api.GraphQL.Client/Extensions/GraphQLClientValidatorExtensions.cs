using System.Reflection;
using System.Runtime.CompilerServices;

namespace Trax.Api.GraphQL.Client;

public static class GraphQLClientValidatorExtensions
{
    /// <summary>
    /// Validates every <see cref="IGenericGraphQLClientRequest"/> type discovered in the given
    /// assemblies. Request types are instantiated via <see cref="RuntimeHelpers.GetUninitializedObject"/>
    /// so this works for types whose constructors take parameters — note that <c>Query</c> must
    /// not depend on any instance fields populated by the constructor.
    /// </summary>
    public static Task ValidateAssembliesAsync(
        this IGraphQLClientValidator validator,
        IEnumerable<Assembly> assemblies,
        CancellationToken cancellationToken = default
    ) => validator.ValidateAssembliesAsync(assemblies, typeFilter: null, cancellationToken);

    public static async Task ValidateAssembliesAsync(
        this IGraphQLClientValidator validator,
        IEnumerable<Assembly> assemblies,
        Func<Type, bool>? typeFilter,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(validator);
        ArgumentNullException.ThrowIfNull(assemblies);

        foreach (var assembly in assemblies)
        {
            var requestTypes = assembly
                .GetTypes()
                .Where(t => t.IsClass && !t.IsAbstract)
                .Where(t => typeof(IGenericGraphQLClientRequest).IsAssignableFrom(t));

            if (typeFilter is not null)
                requestTypes = requestTypes.Where(typeFilter);

            foreach (var type in requestTypes)
            {
                var instance = (IGenericGraphQLClientRequest)
                    RuntimeHelpers.GetUninitializedObject(type);
                await validator
                    .ValidateAsync(instance.Query, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
    }
}
