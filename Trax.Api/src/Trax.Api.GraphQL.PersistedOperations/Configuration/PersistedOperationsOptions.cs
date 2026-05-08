namespace Trax.Api.GraphQL.PersistedOperations.Configuration;

/// <summary>
/// Resolved configuration produced by <see cref="PersistedOperationsBuilder"/>.
/// Registered as a singleton in DI; consumed by the request middleware,
/// storage layer, and broadcaster wiring.
/// </summary>
public sealed class PersistedOperationsOptions
{
    /// <summary>
    /// When true, requests carrying an inline <c>query</c> body (rather than
    /// referencing a persisted operation by id) are rejected with
    /// <c>PERSISTED_OPERATION_REQUIRED</c>. Allowlist and introspection
    /// detection still bypass this.
    /// </summary>
    public bool RequirePersisted { get; internal set; } = true;

    /// <summary>
    /// When true, every inline-query request that would be rejected (or that
    /// is allowed because <see cref="RequirePersisted"/> is false) is logged
    /// at Information level. Use during phased rollout to observe traffic
    /// before flipping enforcement on.
    /// </summary>
    public bool LogNonPersistedRequests { get; internal set; }

    /// <summary>
    /// Operation names that bypass enforcement unconditionally. Case-sensitive.
    /// </summary>
    public IReadOnlySet<string> AllowedOperationNames { get; internal set; } =
        new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Predicates evaluated against the operation name (or, when no name is
    /// available, the document id). Any matching predicate bypasses enforcement.
    /// </summary>
    public IReadOnlyList<Func<string, bool>> AllowOperationPredicates { get; internal set; } =
        Array.Empty<Func<string, bool>>();

    /// <summary>
    /// When true, requests that look like introspection (operation name
    /// <c>IntrospectionQuery</c>, or a query body whose top-level selection
    /// set is purely <c>__schema</c> / <c>__type</c>) bypass enforcement.
    /// Default is true; consumers wanting strict prod can opt out via
    /// <c>DisableIntrospection()</c>.
    /// </summary>
    public bool AllowIntrospection { get; internal set; } = true;

    /// <summary>
    /// When true, the storage layer wraps DB lookups with an in-memory cache.
    /// Default is false: every request hits the database. Caching is purely
    /// an optimization and never the correctness path.
    /// </summary>
    public bool CacheEnabled { get; internal set; }

    /// <summary>
    /// In-memory cache TTL when <see cref="CacheEnabled"/> is true. Acts as a
    /// backstop for the broadcaster: if a node misses an invalidation message,
    /// the entry self-heals when the TTL expires. Defaults to 15 minutes.
    /// </summary>
    public TimeSpan CacheTtl { get; internal set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// RabbitMQ connection string for cross-node cache invalidation. Null when
    /// no broadcaster is configured (single-node deployments). Required when
    /// <see cref="CacheEnabled"/> is true and the deployment runs more than
    /// one node.
    /// </summary>
    public string? RabbitMqConnectionString { get; internal set; }

    /// <summary>
    /// Database connection string for <c>trax.persisted_operation</c> reads
    /// and writes. Required.
    /// </summary>
    public string DatabaseConnectionString { get; internal set; } = string.Empty;
}
