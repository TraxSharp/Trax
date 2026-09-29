namespace Trax.Api.GraphQL.Client;

/// <summary>
/// The non-generic view of an outbound GraphQL request: its query text and variables. Used to scan
/// and validate requests without knowing their response type; implement
/// <see cref="IGraphQLClientRequest{TResponse}"/> rather than this.
/// </summary>
public interface IGenericGraphQLClientRequest
{
    /// <summary>
    /// The GraphQL document to send. Its first definition must be the operation. Keep it constant and
    /// pass values through <see cref="Variables"/>: validation results are cached per query text.
    /// </summary>
    string Query { get; }

    /// <summary>The operation's variables, serialized as the <c>variables</c> object; <c>null</c> (the default) for none.</summary>
    object? Variables => null;
}
