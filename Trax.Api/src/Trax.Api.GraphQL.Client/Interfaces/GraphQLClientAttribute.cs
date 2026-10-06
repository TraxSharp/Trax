using System.Reflection;

namespace Trax.Api.GraphQL.Client;

/// <summary>
/// Names the keyed client a request belongs to, the key passed to
/// <see cref="ServiceExtensions.AddKeyedTraxGraphQLClient"/>. Validating a keyed client's
/// requests (<c>UseStartupValidation</c> on its builder, or the keyed
/// <see cref="ServiceExtensions.ValidateGraphQLClientAssembliesAsync(IServiceProvider, object, Assembly[])"/>)
/// checks only the request types marked with that key; the unkeyed client's validation checks
/// only the types that carry no mark. Running a request does not read it: the executor you
/// resolve decides which server it goes to.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class GraphQLClientAttribute : Attribute
{
    /// <summary>Marks the request as belonging to the client registered under <paramref name="key"/>.</summary>
    /// <param name="key">
    /// The service key, compared with <see cref="object.Equals(object?, object?)"/>: a string or
    /// an enum value, matching the one the client was registered with.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="key"/> is <c>null</c>.</exception>
    public GraphQLClientAttribute(object key)
    {
        ArgumentNullException.ThrowIfNull(key);
        Key = key;
    }

    /// <summary>The service key of the client the request belongs to.</summary>
    public object Key { get; }

    /// <summary>
    /// Whether <paramref name="requestType"/> belongs to the client registered under
    /// <paramref name="serviceKey"/>: a marked type to the client with its key, an unmarked type
    /// to the unkeyed client (<paramref name="serviceKey"/> is <c>null</c>).
    /// </summary>
    internal static bool BelongsTo(Type requestType, object? serviceKey)
    {
        var mark = requestType.GetCustomAttribute<GraphQLClientAttribute>(inherit: false);
        return mark is null ? serviceKey is null : Equals(mark.Key, serviceKey);
    }
}
