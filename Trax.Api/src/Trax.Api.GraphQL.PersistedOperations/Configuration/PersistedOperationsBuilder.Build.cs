namespace Trax.Api.GraphQL.PersistedOperations.Configuration;

public sealed partial class PersistedOperationsBuilder
{
    /// <summary>
    /// Validates configuration and produces the resolved
    /// <see cref="PersistedOperationsOptions"/> consumed by the runtime.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the builder is in an internally inconsistent state. Each
    /// message names the misconfigured method, explains the constraint, and
    /// suggests a fix.
    /// </exception>
    internal PersistedOperationsOptions Build()
    {
        if (!_requirePersisted && !_logNonPersistedRequests)
            throw new InvalidOperationException(
                "Persisted operations is configured but does nothing. "
                    + "Either enable enforcement with RequirePersisted(true), "
                    + "or enable shadow logging with LogNonPersistedRequests(true)."
            );

        if (_allowedOperationNames.Any(string.IsNullOrEmpty))
            throw new InvalidOperationException(
                "AllowOperations rejects empty/null entries. "
                    + "Pass non-empty operation names only."
            );

        if (_rabbitMqConnectionString is null && !_singleNode)
            throw new InvalidOperationException(
                "Persisted operations need to know how a change reaches every node. "
                    + "Each node caches the documents it serves for up to the cache's maximum age, so an "
                    + "upload, deactivation or restore made on one node is seen by another at once only "
                    + "if it is broadcast. Call UseRabbitMqInvalidation(connectionString) when more than "
                    + "one node serves this endpoint, or SingleNode() when exactly one process serves it "
                    + "and writes the store."
            );

        if (_rabbitMqConnectionString is not null && _singleNode)
            throw new InvalidOperationException(
                "SingleNode() and UseRabbitMqInvalidation(...) contradict each other. "
                    + "Keep UseRabbitMqInvalidation when more than one node serves this endpoint, "
                    + "or SingleNode() when exactly one does."
            );

        if (_cacheTtl is { } ttl && ttl > _cacheMaxAge)
            throw new InvalidOperationException(
                $"WithInMemoryCache's TTL ({ttl}) is longer than the cache's maximum age ({_cacheMaxAge}). "
                    + "No cache keeps a persisted operation longer than WithCacheMaxAge allows, so this TTL "
                    + "would not take effect. Shorten the TTL, or raise WithCacheMaxAge."
            );

        return new PersistedOperationsOptions
        {
            RequirePersisted = _requirePersisted,
            LogNonPersistedRequests = _logNonPersistedRequests,
            AllowedOperationNames = _allowedOperationNames,
            AllowOperationPredicates = _allowOperationPredicates,
            AllowIntrospection = _allowIntrospection,
            CacheEnabled = _cacheEnabled,
            CacheTtl = _cacheTtl ?? _cacheMaxAge,
            CacheMaxAge = _cacheMaxAge,
            RabbitMqConnectionString = _rabbitMqConnectionString,
            SingleNode = _singleNode,
            ExposeOperationsNamespace = _exposeOperationsNamespace,
        };
    }
}
