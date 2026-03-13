using System.Collections.Concurrent;
using Trax.Mediator.Configuration;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Mediator.Services.ConcurrencyLimiter;

/// <summary>
/// Singleton service that manages per-train and global concurrency limits for RUN executions.
/// Uses <see cref="SemaphoreSlim"/> instances keyed by train interface FullName.
/// </summary>
public class ConcurrencyLimiter : IConcurrencyLimiter
{
    private readonly MediatorConfiguration _configuration;
    private readonly ITrainDiscoveryService _discoveryService;
    private readonly ConcurrentDictionary<string, Lazy<SemaphoreSlim?>> _perTrainSemaphores = new();
    private readonly SemaphoreSlim? _globalSemaphore;

    public ConcurrencyLimiter(
        MediatorConfiguration configuration,
        ITrainDiscoveryService discoveryService
    )
    {
        _configuration = configuration;
        _discoveryService = discoveryService;
        _globalSemaphore = configuration.GlobalMaxConcurrentRun is { } globalLimit
            ? new SemaphoreSlim(globalLimit, globalLimit)
            : null;
    }

    public async Task<IDisposable> AcquireAsync(string trainFullName, CancellationToken ct)
    {
        var perTrainSemaphore = GetOrCreatePerTrainSemaphore(trainFullName);

        // Acquire per-train first, then global — deterministic order prevents deadlocks
        if (perTrainSemaphore is not null)
            await perTrainSemaphore.WaitAsync(ct);

        try
        {
            if (_globalSemaphore is not null)
                await _globalSemaphore.WaitAsync(ct);
        }
        catch
        {
            // If global acquire fails (cancellation), release the per-train permit we already hold
            perTrainSemaphore?.Release();
            throw;
        }

        return new ConcurrencyPermit(perTrainSemaphore, _globalSemaphore);
    }

    private SemaphoreSlim? GetOrCreatePerTrainSemaphore(string trainFullName)
    {
        return _perTrainSemaphores
            .GetOrAdd(
                trainFullName,
                name => new Lazy<SemaphoreSlim?>(() =>
                {
                    var limit = ResolveLimit(name);
                    return limit is { } l ? new SemaphoreSlim(l, l) : null;
                })
            )
            .Value;
    }

    private int? ResolveLimit(string trainFullName)
    {
        // Priority 1: Builder override
        if (_configuration.ConcurrencyOverrides.TryGetValue(trainFullName, out var builderLimit))
            return builderLimit;

        // Priority 2: Attribute on train class
        var registration = _discoveryService
            .DiscoverTrains()
            .FirstOrDefault(r => r.ServiceType.FullName == trainFullName);

        return registration?.MaxConcurrentRun;
    }

    private sealed class ConcurrencyPermit(SemaphoreSlim? perTrain, SemaphoreSlim? global)
        : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
                return;

            // Release in reverse order of acquisition
            global?.Release();
            perTrain?.Release();
        }
    }
}
