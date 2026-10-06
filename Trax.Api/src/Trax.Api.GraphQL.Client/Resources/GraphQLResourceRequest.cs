namespace Trax.Api.GraphQL.Client;

/// <summary>
/// Base class for mode E requests: <c>Query</c> is loaded from a <c>.graphql</c> embedded
/// resource declared via <see cref="GraphQLQueryResourceAttribute"/>. Subclasses provide
/// <c>Variables</c> as usual.
///
/// The resource is loaded lazily on first access of <c>Query</c> and cached statically per
/// request type. Loading does not run in the constructor, so this remains compatible with
/// <see cref="System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(Type)"/>
/// used by <see cref="GraphQLClientValidatorExtensions.ValidateAssembliesAsync(IGraphQLClientValidator, IEnumerable{System.Reflection.Assembly}, CancellationToken)"/>.
/// </summary>
public abstract class GraphQLResourceRequest<TResponse> : IGraphQLClientRequest<TResponse>
{
    /// <summary>
    /// The query text from the embedded resource named by <see cref="GraphQLQueryResourceAttribute"/>,
    /// loaded on first access and cached per request type. Throws
    /// <see cref="InvalidOperationException"/> when the attribute is missing or no matching resource
    /// exists.
    /// </summary>
    public virtual string Query => ResourceQueryCache.GetQuery(GetType());

    /// <summary>The operation's variables; <c>null</c> unless a subclass overrides it.</summary>
    public virtual object? Variables => null;
}
