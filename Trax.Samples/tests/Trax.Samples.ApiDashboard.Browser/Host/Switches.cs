using System.Collections.Concurrent;

namespace Trax.Samples.ApiDashboard.Browser.Host;

/// <summary>
/// The keys whose outside service is down: <see cref="CallSupplier"/> and <see cref="NotifyClaimant"/> fail while
/// theirs is. A singleton of the host, so a seed builder sets it and the host's workers read it.
/// </summary>
public sealed class Outages
{
    private readonly ConcurrentDictionary<string, bool> _down = new(StringComparer.Ordinal);

    public void Down(string key) => _down[key] = true;

    public void Up(string key) => _down.TryRemove(key, out _);

    public bool IsDown(string key) => _down.ContainsKey(key);
}

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
