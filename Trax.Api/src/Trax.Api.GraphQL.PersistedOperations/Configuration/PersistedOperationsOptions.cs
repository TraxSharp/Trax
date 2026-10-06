namespace Trax.Api.GraphQL.PersistedOperations.Configuration;

/// <summary>
/// Resolved configuration produced by <see cref="PersistedOperationsBuilder"/>.
/// Registered as a singleton in DI; consumed by the request middleware,
/// storage layer, and broadcaster wiring.
/// </summary>
public sealed class PersistedOperationsOptions
{
    // Built only by PersistedOperationsBuilder; every property is set there.
    internal PersistedOperationsOptions() { }

    /// <summary>
    /// When true (the default), <c>UsePersistedOperations</c> grafts the <c>operations</c>
    /// namespace onto the schema, which is where the persisted-operation management mutations
    /// live. When false, enforcement and storage are wired without it.
    /// </summary>
    public bool ExposeOperationsNamespace { get; internal set; } = true;

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
    /// Operation names that bypass enforcement unconditionally. Case-sensitive. Matched against
    /// the <c>operationName</c> the caller supplies, not against the document, so this is a
    /// convenience for trusted networks rather than a security control.
    /// </summary>
    public IReadOnlySet<string> AllowedOperationNames { get; internal set; } =
        new HashSet<string>(StringComparer.Ordinal);

    /// <summary>
    /// Predicates evaluated against the operation name (or, when no name is
    /// available, the document id). Any matching predicate bypasses enforcement. Both keys are
    /// supplied by the caller, so this is a convenience for trusted networks rather than a
    /// security control.
    /// </summary>
    public IReadOnlyList<Func<string, bool>> AllowOperationPredicates { get; internal set; } =
        Array.Empty<Func<string, bool>>();

    /// <summary>
    /// When true, requests whose document selects only introspection fields
    /// (<c>__schema</c>, <c>__type</c>, <c>__typename</c>) at the top level bypass
    /// enforcement. Introspection is recognised from the parsed document alone.
    /// Default is true; consumers wanting strict prod can opt out via
    /// <c>DisableIntrospection()</c>.
    /// </summary>
    public bool AllowIntrospection { get; internal set; } = true;

    /// <summary>
    /// When true, the store's lookups are cached in memory as well. Default is false. HotChocolate
    /// caches the parsed document and prepared operation for each id either way, so with this off
    /// the database is read when a node first serves an id and after a change empties those
    /// caches, not on every request.
    /// </summary>
    public bool CacheEnabled { get; internal set; }

    /// <summary>
    /// In-memory cache TTL when <see cref="CacheEnabled"/> is true. Defaults to
    /// <see cref="CacheMaxAge"/>, and never exceeds it.
    /// </summary>
    public TimeSpan CacheTtl { get; internal set; } = DefaultCacheMaxAge;

    /// <summary>
    /// The longest any persisted-operation cache on a node keeps an entry, counted from when its
    /// document was read from the database: HotChocolate's parsed-document and prepared-operation
    /// caches, the lookup cache, and the record of ids the store does not hold. Defaults to five
    /// minutes. The backstop for a change that did not reach this node.
    /// </summary>
    public TimeSpan CacheMaxAge { get; internal set; } = DefaultCacheMaxAge;

    internal static readonly TimeSpan DefaultCacheMaxAge = TimeSpan.FromMinutes(5);

    /// <summary>
    /// RabbitMQ connection string for cross-node invalidation, set by
    /// <c>UseRabbitMqInvalidation</c>. Every node empties its persisted-operation caches when a
    /// change is broadcast on it. Null only when the host declared <see cref="SingleNode"/>.
    /// </summary>
    public string? RabbitMqConnectionString { get; internal set; }

    /// <summary>
    /// The fanout exchange invalidations are broadcast on. Every node uses the same name so they
    /// meet without configuration; tests give each run its own so runs on one broker stay apart.
    /// </summary>
    internal string RabbitMqExchange { get; set; } =
        Broadcasting.RabbitMqPersistedOperationBroadcaster.ExchangeName;

    /// <summary>
    /// True when the host declared, with <c>SingleNode()</c>, that one process serves this
    /// endpoint and writes the store, so no broadcast is needed.
    /// </summary>
    public bool SingleNode { get; internal set; }

    /// <summary>
    /// Not used, and always empty. Persisted operations read and write through the Trax data
    /// context that <c>AddEffects(e =&gt; e.UsePostgres(...))</c> registers.
    /// </summary>
    [Obsolete("Not used: persisted operations read and write through the Trax data context.")]
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public string DatabaseConnectionString { get; internal set; } = string.Empty;
}
