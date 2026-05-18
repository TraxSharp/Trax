namespace Trax.Api.GraphQL.Client;

/// <summary>
/// Base class for mode E requests: <c>Query</c> is loaded from a <c>.graphql</c> embedded
/// resource declared via <see cref="GraphQLQueryResourceAttribute"/>. Subclasses provide
/// <c>Variables</c> as usual.
///
/// The resource is loaded lazily on first access of <c>Query</c> and cached statically per
/// request type. Loading does not run in the constructor, so this remains compatible with
/// <see cref="System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(Type)"/>
/// used by <see cref="GraphQLClientValidatorExtensions.ValidateAssembliesAsync"/>.
/// </summary>
public abstract class GraphQLResourceRequest<TResponse> : IGraphQLClientRequest<TResponse>
{
    public virtual string Query => ResourceQueryCache.GetQuery(GetType());

    public virtual object? Variables => null;
}
