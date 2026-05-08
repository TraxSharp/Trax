namespace Trax.Api.GraphQL.PersistedOperations.Configuration;

public sealed partial class PersistedOperationsBuilder
{
    /// <summary>
    /// Wire a RabbitMQ broadcaster for cross-node cache invalidation. Only
    /// meaningful when <see cref="WithInMemoryCache"/> has also been called.
    /// </summary>
    public PersistedOperationsBuilder UseRabbitMqInvalidation(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new ArgumentException(
                "UseRabbitMqInvalidation requires a connection string.",
                nameof(connectionString)
            );

        _rabbitMqConnectionString = connectionString;
        return this;
    }
}
