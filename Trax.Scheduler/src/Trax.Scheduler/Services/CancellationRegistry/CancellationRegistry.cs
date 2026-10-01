using System.Collections.Concurrent;

namespace Trax.Scheduler.Services.CancellationRegistry;

/// <inheritdoc />
internal class CancellationRegistry : ICancellationRegistry
{
    private readonly ConcurrentDictionary<long, CancellationTokenSource> _registry = new();

    /// <remarks>
    /// The first registration for an id holds it until it is unregistered. A second delivery of
    /// the same job does not run the train, so taking the id from the delivery that does would
    /// leave the running train out of reach of a cancel.
    /// </remarks>
    public void Register(long metadataId, CancellationTokenSource cts) =>
        _registry.TryAdd(metadataId, cts);

    public void Unregister(long metadataId) => _registry.TryRemove(metadataId, out _);

    public void Unregister(long metadataId, CancellationTokenSource cts) =>
        _registry.TryRemove(new KeyValuePair<long, CancellationTokenSource>(metadataId, cts));

    public bool TryCancel(long metadataId)
    {
        if (!_registry.TryGetValue(metadataId, out var cts))
            return false;

        cts.Cancel();
        return true;
    }
}
