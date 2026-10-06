namespace Trax.Api.GraphQL.PersistedOperations.Broadcasting;

/// <summary>
/// Default broadcaster: silently swallows every publish. Wired when the
/// consumer does not configure multi-node invalidation.
/// </summary>
internal sealed class NoOpPersistedOperationBroadcaster : IPersistedOperationBroadcaster
{
    public Task PublishAsync(PersistedOperationChangedMessage message, CancellationToken ct) =>
        Task.CompletedTask;
}
