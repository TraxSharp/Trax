using System.Reflection;
using System.Runtime.CompilerServices;

namespace Trax.Api.GraphQL.Client;

/// <summary>
/// Validates every request type in a set of assemblies against the client's schema, so a query the
/// server no longer accepts fails at startup or in a test rather than on first use.
/// </summary>
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

    /// <summary>
    /// Validates every concrete <see cref="IGenericGraphQLClientRequest"/> type in
    /// <paramref name="assemblies"/> that <paramref name="typeFilter"/> accepts. Types are created
    /// without running a constructor, so <c>Query</c> must not depend on constructor state. Stops at
    /// the first failure.
    /// </summary>
    /// <param name="validator">The client's validator.</param>
    /// <param name="assemblies">The assemblies to scan.</param>
    /// <param name="typeFilter">Which request types to validate; <c>null</c> for all.</param>
    /// <param name="cancellationToken">Cancels validation.</param>
    /// <exception cref="GraphQLValidationException">A request's query is not valid against the schema.</exception>
    /// <exception cref="GraphQLSchemaIntrospectionException">The schema could not be loaded.</exception>
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
