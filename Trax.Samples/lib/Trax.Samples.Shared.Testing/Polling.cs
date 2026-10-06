using System.Diagnostics;

namespace Trax.Samples.Shared.Testing;

/// <summary>
/// Deterministic waiting helpers. Tests synchronise on a completion condition rather than sleeping
/// for a fixed duration, so they finish as soon as the condition holds and fail loudly (not flakily)
/// when it never does.
/// </summary>
public static class Polling
{
    /// <summary>
    /// Polls <paramref name="condition"/> until it returns true or <paramref name="timeout"/>
    /// elapses. Returns true if the condition was met. The caller must assert on the return value so
    /// a timeout fails the test instead of passing silently.
    /// </summary>
    public static async Task<bool> WaitUntilAsync(
        Func<Task<bool>> condition,
        TimeSpan timeout,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(condition);

        var interval = pollInterval ?? TimeSpan.FromMilliseconds(25);
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            if (await condition())
                return true;

            await Task.Delay(interval, cancellationToken);
        }

        return await condition();
    }

    /// <summary>Synchronous-condition overload of <see cref="WaitUntilAsync(Func{Task{bool}}, TimeSpan, TimeSpan?, CancellationToken)"/>.</summary>
    public static Task<bool> WaitUntilAsync(
        Func<bool> condition,
        TimeSpan timeout,
        TimeSpan? pollInterval = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(condition);
        return WaitUntilAsync(
            () => Task.FromResult(condition()),
            timeout,
            pollInterval,
            cancellationToken
        );
    }
}
