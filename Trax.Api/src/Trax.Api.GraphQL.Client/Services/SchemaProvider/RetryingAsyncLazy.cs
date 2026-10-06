namespace Trax.Api.GraphQL.Client;

/// <summary>
/// A value loaded once on first request and shared, except that a load which fails or is
/// cancelled is not kept. This is the retry-on-failure form of an async lazy (Nito.AsyncEx's
/// <c>AsyncLazyFlags.RetryOnFailure</c>), since a <see cref="Lazy{T}"/> over a task would hand
/// every later caller the same faulted task.
///
/// <para>A failed load is retried after a backoff, not on the next request: until then a request
/// gets the failure at once, so a server that is down is not introspected once per outbound
/// query. The backoff doubles with each consecutive failure from <see cref="BaseBackoff"/> up to
/// <see cref="MaxBackoff"/>, with equal jitter (half the delay fixed, half random) so many
/// processes that failed together do not retry together. A load that timed out counts as failed.</para>
///
/// <para>A caller's token cancels only that caller's wait, never the shared load, so one
/// impatient caller cannot fail the load for every other caller waiting on it (the behaviour of
/// <c>Microsoft.VisualStudio.Threading.AsyncLazy.GetValueAsync(CancellationToken)</c>).</para>
/// </summary>
internal sealed class RetryingAsyncLazy<T>
{
    /// <summary>The longest delay before the first retry.</summary>
    internal static readonly TimeSpan BaseBackoff = TimeSpan.FromSeconds(1);

    /// <summary>The longest delay before any retry.</summary>
    internal static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    private readonly Func<Task<T>> _factory;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private Task<T>? _load;
    private int _failures;
    private DateTimeOffset _retryAt;

    public RetryingAsyncLazy(Func<Task<T>> factory, TimeProvider time)
    {
        _factory = factory;
        _time = time;
    }

    public Task<T> GetValueAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<T>(cancellationToken);

        Task<T> load;
        lock (_gate)
        {
            if (_load is null || ((_load.IsFaulted || _load.IsCanceled) && Retryable()))
                _load = LoadAsync();
            load = _load;
        }

        return cancellationToken.CanBeCanceled ? load.WaitAsync(cancellationToken) : load;
    }

    /// <summary>
    /// The delay after the <paramref name="failures"/>th consecutive failure: the capped
    /// exponential delay, of which half is fixed and half random.
    /// </summary>
    internal static TimeSpan Backoff(int failures)
    {
        var exponent = Math.Min(failures - 1, 30);
        var ceiling = Math.Min(
            MaxBackoff.TotalMilliseconds,
            BaseBackoff.TotalMilliseconds * Math.Pow(2, exponent)
        );
        return TimeSpan.FromMilliseconds(ceiling / 2 + Random.Shared.NextDouble() * ceiling / 2);
    }

    private bool Retryable() => _time.GetUtcNow() >= _retryAt;

    // The outcome is recorded before the returned task completes, so a caller that sees the
    // failure and asks again at once already meets its backoff.
    private async Task<T> LoadAsync()
    {
        try
        {
            // Task.Run so the factory's synchronous part never runs under the lock, and a
            // synchronous throw becomes a faulted task like any other failure.
            var value = await Task.Run(_factory).ConfigureAwait(false);
            lock (_gate)
                _failures = 0;
            return value;
        }
        catch
        {
            lock (_gate)
            {
                _failures++;
                _retryAt = _time.GetUtcNow() + Backoff(_failures);
            }
            throw;
        }
    }
}
