namespace Trax.Mediator.Services.ConcurrencyLimiter;

/// <summary>
/// Gates concurrent RUN executions per train, per principal and globally.
/// Acquire a permit before executing a train; dispose it when done. Queued work does not pass
/// through it. Infrastructure used by the train execution service; not intended to be called
/// directly.
/// </summary>
public interface IConcurrencyLimiter
{
    /// <summary>
    /// Acquires a concurrency permit for the given train. Waits (asynchronously) while the
    /// per-train, per-principal or global limit is reached, taking them in that order. The
    /// principal is the one <see cref="Principal.ICurrentPrincipalProvider"/> reports at the time
    /// of the call. Dispose the returned permit to release every slot it holds; disposing twice
    /// is safe.
    /// </summary>
    /// <param name="trainFullName">The canonical interface FullName of the train.</param>
    /// <param name="ct">Cancellation token — throws <see cref="OperationCanceledException"/> if cancelled while waiting.</param>
    /// <returns>A disposable permit that releases the concurrency slot on dispose.</returns>
    Task<IDisposable> AcquireAsync(string trainFullName, CancellationToken ct);
}
