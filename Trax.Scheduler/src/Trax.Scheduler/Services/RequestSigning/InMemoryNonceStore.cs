using System.Collections.Concurrent;

namespace Trax.Scheduler.Services.RequestSigning;

/// <summary>
/// An <see cref="INonceStore"/> held in this process. Correct only for a runner that runs as a
/// single instance: a second instance keeps its own memory and accepts the same request again.
/// Chosen with <c>UseInMemoryNonceStore()</c> (see scheduler/0009).
/// </summary>
internal sealed class InMemoryNonceStore : INonceStore
{
    private readonly ConcurrentDictionary<string, long> _seen = new(StringComparer.Ordinal);
    private long _recordedSinceSweep;

    /// <inheritdoc />
    public ValueTask<bool> TryRecordAsync(
        string nonce,
        DateTimeOffset expiresAt,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        var expires = expiresAt.ToUnixTimeSeconds();
        var current = now.ToUnixTimeSeconds();

        while (!_seen.TryAdd(nonce, expires))
        {
            // Removed by a sweep since TryAdd failed: try the add again.
            if (!_seen.TryGetValue(nonce, out var existing))
                continue;

            if (existing >= current)
                return ValueTask.FromResult(false);

            // A record past its expiry no longer refuses anything, so it is replaced rather than
            // left for the sweep. Losing the race to another caller goes round again.
            if (_seen.TryUpdate(nonce, expires, existing))
                break;
        }

        if (Interlocked.Increment(ref _recordedSinceSweep) % 256 == 0)
            SweepExpired(current);

        return ValueTask.FromResult(true);
    }

    private void SweepExpired(long now)
    {
        foreach (var (nonce, expires) in _seen)
            if (expires < now)
                _seen.TryRemove(new KeyValuePair<string, long>(nonce, expires));
    }
}
