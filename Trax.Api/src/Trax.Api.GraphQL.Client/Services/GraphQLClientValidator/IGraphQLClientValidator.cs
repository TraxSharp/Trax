using GraphQLParser.AST;

namespace Trax.Api.GraphQL.Client;

public interface IGraphQLClientValidator
{
    /// <summary>
    /// Validates the query against the configured schema and returns its operation type.
    /// Results are cached by query string; queries must be parameterized via variables
    /// to keep the cache bounded.
    /// </summary>
    Task<OperationType> ValidateAsync(string query, CancellationToken cancellationToken = default);
}
