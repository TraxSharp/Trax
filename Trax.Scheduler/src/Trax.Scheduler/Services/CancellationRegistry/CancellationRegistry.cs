using System.Collections.Concurrent;

namespace Trax.Scheduler.Services.CancellationRegistry;

/// <inheritdoc />
internal class CancellationRegistry : ICancellationRegistry
{
    private readonly ConcurrentDictionary<long, CancellationTokenSource> _registry = new();

    public void Register(long metadataId, CancellationTokenSource cts) =>
        _registry[metadataId] = cts;

    public void Unregister(long metadataId) => _registry.TryRemove(metadataId, out _);

    public bool TryCancel(long metadataId)
    {
        if (!_registry.TryGetValue(metadataId, out var cts))
            return false;

        cts.Cancel();
        return true;
    }
}
