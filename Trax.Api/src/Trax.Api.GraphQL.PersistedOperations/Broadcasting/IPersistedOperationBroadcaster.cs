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
    /// Publish an invalidation event. Must not throw on transport failures
    /// (the local DB write has already succeeded; broadcaster errors should
    /// be logged but never fail the user-visible operation).
    /// </summary>
    Task PublishAsync(PersistedOperationChangedMessage message, CancellationToken ct);
}
