namespace Trax.Api.GraphQL.Client;

/// <summary>
/// Runs outbound GraphQL requests: validates the query against the server's schema, sends it, and
/// reads the result. Registered as a singleton by <c>AddTraxGraphQLClient</c> (keyed, by
/// <c>AddKeyedTraxGraphQLClient</c>) and safe to use concurrently.
/// </summary>
public interface IGraphQLClientExecutor
{
    /// <summary>
    /// Validates <paramref name="request"/>'s query, sends it as a query or mutation according to its
    /// operation type, and extracts the result. Under a strict <see cref="ResponseStrictness"/> the
    /// top level of the result is also checked against <typeparamref name="TReturn"/>'s properties.
    /// </summary>
    /// <typeparam name="TReturn">The response type.</typeparam>
    /// <param name="request">The request to run.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The extracted response; <c>default</c> when the unwrapped value is JSON <c>null</c>.</returns>
    /// <exception cref="GraphQLValidationException">The query is not valid against the schema.</exception>
    /// <exception cref="GraphQLExecutionException">The server returned errors or no data, or the data could not be extracted.</exception>
    /// <exception cref="GraphQLResponseShapeException">The response shape drifted under <see cref="ResponseStrictness.ThrowOnDrift"/>.</exception>
    /// <exception cref="NotSupportedException">The operation is a subscription.</exception>
    Task<TReturn> Run<TReturn>(
        IGraphQLClientRequest<TReturn> request,
        CancellationToken cancellationToken = default
    );
}
