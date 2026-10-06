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
    /// Validates every concrete, closed <see cref="IGenericGraphQLClientRequest"/> type in
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

        await ValidateMatchingAsync(validator, assemblies, typeFilter, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Validates the request types that belong to the client registered under
    /// <paramref name="serviceKey"/> (see <see cref="GraphQLClientAttribute"/>): for a key, the
    /// types marked with it; for <c>null</c>, the unmarked types. Finding none is refused, since a
    /// validation that checks nothing is a request someone forgot to mark, not a passing check.
    /// </summary>
    /// <param name="validator">The client's validator.</param>
    /// <param name="assemblies">The assemblies to scan.</param>
    /// <param name="serviceKey">The client's key, or <c>null</c> for the unkeyed client.</param>
    /// <param name="isRegisteredKey">
    /// Whether a client is registered under a key. Every key a request in
    /// <paramref name="assemblies"/> is marked with must be one, or that request is validated by
    /// no client.
    /// </param>
    /// <param name="cancellationToken">Cancels validation.</param>
    /// <exception cref="InvalidOperationException">No request type in <paramref name="assemblies"/> belongs to the client, or a request is marked with a key no client is registered under.</exception>
    internal static async Task ValidateClientRequestsAsync(
        this IGraphQLClientValidator validator,
        IReadOnlyCollection<Assembly> assemblies,
        object? serviceKey,
        Func<object, bool> isRegisteredKey,
        CancellationToken cancellationToken
    )
    {
        var validated = await ValidateMatchingAsync(
                validator,
                assemblies,
                t => GraphQLClientAttribute.BelongsTo(t, serviceKey),
                cancellationToken
            )
            .ConfigureAwait(false);

        if (validated > 0)
        {
            RefuseUnregisteredKeys(assemblies, isRegisteredKey);
            return;
        }

        var scanned = string.Join(", ", assemblies.Select(a => a.GetName().Name));
        throw new InvalidOperationException(
            serviceKey is null
                ? $"Validation for the unkeyed GraphQL client found no unmarked request type in {scanned}. "
                    + "A request marked [GraphQLClient(key)] is validated only by the client registered "
                    + "under that key; pass the assembly that holds this client's requests."
                : $"Validation for the GraphQL client keyed '{serviceKey}' found no request type marked "
                    + $"[GraphQLClient({serviceKey})] in {scanned}. Mark each request this server answers with "
                    + "[GraphQLClient(...)] and the key the client was registered with."
        );
    }

    /// <summary>
    /// Refuses a request marked with a key no client is registered under: the validation for
    /// every registered client passes over it, so a mistyped key would leave it unchecked.
    /// </summary>
    private static void RefuseUnregisteredKeys(
        IEnumerable<Assembly> assemblies,
        Func<object, bool> isRegisteredKey
    )
    {
        var orphans = RequestTypes(assemblies)
            .Select(t => (Type: t, Mark: t.GetCustomAttribute<GraphQLClientAttribute>(false)))
            .Where(r => r.Mark is not null && !isRegisteredKey(r.Mark.Key))
            .Select(r => $"{r.Type.FullName} [GraphQLClient({r.Mark!.Key})]")
            .ToList();

        if (orphans.Count == 0)
            return;

        throw new InvalidOperationException(
            "A request is marked for a GraphQL client that is not registered, so no client "
                + "validates it: "
                + string.Join(", ", orphans)
                + ". Register a client under that key with AddKeyedTraxGraphQLClient, or correct "
                + "the key in the mark."
        );
    }

    private static IEnumerable<Type> RequestTypes(IEnumerable<Assembly> assemblies) =>
        assemblies
            .SelectMany(a => a.GetTypes())
            // An open generic type is a shape closed requests share, not a request: it has no
            // query of its own and cannot be instantiated.
            .Where(t => t.IsClass && !t.IsAbstract && !t.ContainsGenericParameters)
            .Where(t => typeof(IGenericGraphQLClientRequest).IsAssignableFrom(t));

    private static async Task<int> ValidateMatchingAsync(
        IGraphQLClientValidator validator,
        IEnumerable<Assembly> assemblies,
        Func<Type, bool>? typeFilter,
        CancellationToken cancellationToken
    )
    {
        var validated = 0;
        var requestTypes = RequestTypes(assemblies);
        if (typeFilter is not null)
            requestTypes = requestTypes.Where(typeFilter);

        foreach (var type in requestTypes)
        {
            var instance = (IGenericGraphQLClientRequest)
                RuntimeHelpers.GetUninitializedObject(type);
            await validator.ValidateAsync(instance.Query, cancellationToken).ConfigureAwait(false);
            validated++;
        }
        return validated;
    }
}
