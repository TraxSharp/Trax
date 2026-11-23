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
