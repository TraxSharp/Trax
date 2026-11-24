using System.Collections.Concurrent;

namespace Trax.Scheduler.Sandbox.Launcher.Core;

/// <summary>
/// The nonces of requests the launcher accepted within the freshness window, so a captured
/// request cannot start a second sandbox. In memory: run one launcher replica (see the docs).
/// </summary>
internal sealed class NonceCache(TimeProvider time)
{
    /// <summary>How far a request's timestamp may be from the launcher's clock, either way.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, long> _seen = new(StringComparer.Ordinal);

    /// <summary>Whether <paramref name="timestamp"/> is within the window of now.</summary>
    public bool IsFresh(long timestamp)
    {
        var now = time.GetUtcNow().ToUnixTimeSeconds();
        return Math.Abs(now - timestamp) <= (long)Window.TotalSeconds;
    }

    /// <summary>Records <paramref name="nonce"/>; false when it was already recorded.</summary>
    public bool TryAccept(string nonce, long timestamp)
    {
        Evict();
        return _seen.TryAdd(nonce, timestamp);
    }

    private void Evict()
    {
        var oldest = time.GetUtcNow().ToUnixTimeSeconds() - 2 * (long)Window.TotalSeconds;
        foreach (var (nonce, at) in _seen)
            if (at < oldest)
                _seen.TryRemove(nonce, out _);
    }
}
