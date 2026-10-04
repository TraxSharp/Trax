namespace Trax.Api.GraphQL.PersistedOperations.Broadcasting;

/// <summary>
/// Publishes <see cref="PersistedOperationChangedMessage"/> events so that
/// other nodes can invalidate their local cache. The default registration
/// is a no-op; the RabbitMQ implementation is wired only when the consumer
/// calls <c>UseRabbitMqInvalidation()</c>.
/// </summary>
public interface IPersistedOperationBroadcaster
{
    /// <summary>
    /// Publish an invalidation event, and complete only once the transport has taken
    /// responsibility for it (for RabbitMQ, the broker's publisher confirm). Throws when it could
    /// not: the store has already saved the change, and reports it to its caller as
    /// <c>CHANGE_NOT_BROADCAST</c> so the operator knows the other nodes were not told.
    /// </summary>
    Task PublishAsync(PersistedOperationChangedMessage message, CancellationToken ct);
}
