using GraphQLParser.AST;

namespace Trax.Api.GraphQL.Client;

/// <summary>
/// Checks outbound queries against the server's schema before they are sent. Registered per client
/// by <c>AddTraxGraphQLClient</c>; <see cref="GraphQLClientValidatorExtensions"/> uses it to
/// validate whole assemblies.
/// </summary>
public interface IGraphQLClientValidator
{
    /// <summary>
    /// Validates the query against the configured schema and returns its operation type.
    /// The document must contain exactly one operation, in any position among its fragments,
    /// because requests are sent without an operation name; otherwise it throws
    /// <see cref="GraphQLValidationException"/>. Results are cached by query string; queries
    /// must be parameterized via variables to keep the cache bounded.
    /// </summary>
    Task<OperationType> ValidateAsync(string query, CancellationToken cancellationToken = default);
}
