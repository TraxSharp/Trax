namespace Trax.Api.GraphQL.PersistedOperations.Configuration;

public sealed partial class PersistedOperationsBuilder
{
    /// <summary>
    /// Broadcast every persisted-operation change over RabbitMQ, so each node empties its
    /// persisted-operation caches when any node uploads, deactivates or restores an operation.
    /// </summary>
    /// <remarks>
    /// Every node caches the documents it serves: HotChocolate's parsed-document and
    /// prepared-operation caches always, and the Trax lookup cache when
    /// <see cref="WithInMemoryCache"/> is on, each for at most <see cref="WithCacheMaxAge"/>. A
    /// change made on one node reaches the others at once only through this broadcast; the
    /// maximum age is the backstop when a broadcast is lost. A publish the broker does not confirm
    /// is reported to the caller as <c>CHANGE_NOT_BROADCAST</c>. Each node binds its own
    /// queue to a fanout exchange; when the connection to the broker drops, the node empties its
    /// caches, and empties them again once the connection recovers, because a change broadcast
    /// in between was not delivered to it.
    /// <para>
    /// A host that runs more than one node needs this; a host that runs one declares
    /// <see cref="SingleNode"/> instead. Persisted operations refuse to start with neither.
    /// </para>
    /// </remarks>
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

    /// <summary>
    /// Declare that exactly one process serves this endpoint and writes the persisted-operation
    /// store, so a change made through it is seen by every cache there is.
    /// </summary>
    /// <remarks>
    /// Persisted operations refuse to start unless the host either declares this or calls
    /// <see cref="UseRabbitMqInvalidation"/>. The declaration is a claim about the deployment that
    /// nothing at runtime can check: run a second node, or write the store from another process
    /// (a CI uploader using <c>AddPersistedOperationStore</c>), and a change made there reaches
    /// this node's caches only when the entry reaches its maximum age (<see cref="WithCacheMaxAge"/>).
    /// Such a deployment needs the broadcaster.
    /// </remarks>
    public PersistedOperationsBuilder SingleNode()
    {
        _singleNode = true;
        return this;
    }
}
