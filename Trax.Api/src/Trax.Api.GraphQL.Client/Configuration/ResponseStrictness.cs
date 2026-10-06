namespace Trax.Api.GraphQL.Client;

/// <summary>
/// Controls how aggressively the executor checks that the JSON response shape matches
/// the request's <c>TResponse</c> POCO. Validation runs once per unique request type
/// (cached) and only when the request uses the default <see cref="IGraphQLClientRequest{T}.Extract"/>.
/// </summary>
public enum ResponseStrictness
{
    /// <summary>
    /// Default. The response is deserialized as-is. Extra JSON fields are silently ignored;
    /// missing fields fall back to the POCO's default values. This matches System.Text.Json's
    /// default behavior.
    /// </summary>
    Lenient,

    /// <summary>
    /// Drift is logged at warning level via <c>ILogger&lt;GraphQLClientExecutor&gt;</c>, but the
    /// call still succeeds. Useful in production where a noisy log is preferable to a thrown
    /// exception that would break running workloads.
    /// </summary>
    WarnOnDrift,

    /// <summary>
    /// Drift throws <see cref="GraphQLResponseShapeException"/>. Catches "I added <c>level</c>
    /// to the POCO but forgot to add it to the query" at first response. Recommended for
    /// integration tests and development environments.
    /// </summary>
    ThrowOnDrift,
}
