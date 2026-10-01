namespace Trax.Api.GraphQL.Client;

/// <summary>
/// A value loaded once on first request and shared, except that a load which fails or is
/// cancelled is not kept: the next request starts a new one. This is the retry-on-failure form
/// of an async lazy (Nito.AsyncEx's <c>AsyncLazyFlags.RetryOnFailure</c>), since a
/// <see cref="Lazy{T}"/> over a task would hand every later caller the same faulted task.
///
/// <para>A caller's token cancels only that caller's wait, never the shared load, so one
/// impatient caller cannot fail the load for every other caller waiting on it (the behaviour of
/// <c>Microsoft.VisualStudio.Threading.AsyncLazy.GetValueAsync(CancellationToken)</c>).</para>
/// </summary>
internal sealed class RetryingAsyncLazy<T>
{
    private readonly Func<Task<T>> _factory;
    private readonly object _gate = new();
    private Task<T>? _load;

    public RetryingAsyncLazy(Func<Task<T>> factory)
    {
        _factory = factory;
    }

    public Task<T> GetValueAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<T>(cancellationToken);

        Task<T> load;
        lock (_gate)
        {
            if (_load is null || _load.IsFaulted || _load.IsCanceled)
                // Task.Run so the factory's synchronous part never runs under the lock, and a
                // synchronous throw becomes a faulted task like any other failure.
                _load = Task.Run(_factory);
            load = _load;
        }

        return cancellationToken.CanBeCanceled ? load.WaitAsync(cancellationToken) : load;
    }
}
