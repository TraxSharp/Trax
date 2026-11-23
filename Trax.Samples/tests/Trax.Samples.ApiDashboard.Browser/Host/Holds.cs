using System.Collections.Concurrent;

namespace Trax.Samples.ApiDashboard.Browser.Host;

/// <summary>
/// Keys a run or a queue hook waits on until the test releases them. A key released before anything waits on it
/// stays released, so a run started after the release does not wait.
/// </summary>
public sealed class Holds
{
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _holds = new(
        StringComparer.Ordinal
    );

    public void Hold(string key) =>
        _holds.TryAdd(
            key,
            new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)
        );

    public void Release(string key)
    {
        if (_holds.TryGetValue(key, out var hold))
            hold.TrySetResult();
    }

    /// <summary>Waits until <paramref name="key"/> is released; a key never held does not wait.</summary>
    public Task WaitAsync(string key, CancellationToken cancellationToken) =>
        _holds.TryGetValue(key, out var hold)
            ? hold.Task.WaitAsync(cancellationToken)
            : Task.CompletedTask;
}
