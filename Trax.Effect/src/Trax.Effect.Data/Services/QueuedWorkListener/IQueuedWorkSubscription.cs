namespace Trax.Effect.Data.Services.QueuedWorkListener;

/// <summary>One listening session opened by <see cref="IQueuedWorkListener.SubscribeAsync"/>.</summary>
public interface IQueuedWorkSubscription : IAsyncDisposable
{
    /// <summary>
    /// Completes when a notice arrives. Throws when the session is lost, for example when its
    /// database connection is closed; dispose it and subscribe again.
    /// </summary>
    Task WaitAsync(CancellationToken cancellationToken);
}
