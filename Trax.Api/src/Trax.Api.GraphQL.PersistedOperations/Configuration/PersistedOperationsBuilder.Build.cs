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

        if (_rabbitMqConnectionString is not null && !_cacheEnabled)
            throw new InvalidOperationException(
                "UseRabbitMqInvalidation requires WithInMemoryCache() to be configured first. "
                    + "Broadcasts have nothing to invalidate without a cache layer. "
                    + "Either remove UseRabbitMqInvalidation, or add WithInMemoryCache() before it."
            );

        if (string.IsNullOrWhiteSpace(_databaseConnectionString))
            throw new InvalidOperationException(
                "UseDatabase(connectionString) is required. "
                    + "The persisted-operations storage layer reads and writes against trax.persisted_operation."
            );

        return new PersistedOperationsOptions
        {
            RequirePersisted = _requirePersisted,
            LogNonPersistedRequests = _logNonPersistedRequests,
            AllowedOperationNames = _allowedOperationNames,
            AllowOperationPredicates = _allowOperationPredicates,
            AllowIntrospection = _allowIntrospection,
            CacheEnabled = _cacheEnabled,
            CacheTtl = _cacheTtl,
            RabbitMqConnectionString = _rabbitMqConnectionString,
            DatabaseConnectionString = _databaseConnectionString!,
        };
    }
}
