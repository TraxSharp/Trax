namespace Trax.Mediator.Services.ConcurrencyLimiter;

/// <summary>
/// Gates concurrent RUN executions per train and/or globally.
/// Acquire a permit before executing a train; dispose it when done.
/// </summary>
public interface IConcurrencyLimiter
{
    /// <summary>
    /// Acquires a concurrency permit for the given train. Blocks (async) if the
    /// per-train or global limit is reached. Dispose the returned permit to release the slot.
    /// </summary>
    /// <param name="trainFullName">The canonical interface FullName of the train.</param>
    /// <param name="ct">Cancellation token — throws <see cref="OperationCanceledException"/> if cancelled while waiting.</param>
    /// <returns>A disposable permit that releases the concurrency slot on dispose.</returns>
    Task<IDisposable> AcquireAsync(string trainFullName, CancellationToken ct);
}
