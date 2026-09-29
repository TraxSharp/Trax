using System.Collections.Concurrent;
using Trax.Mediator.Configuration;
using Trax.Mediator.Services.Principal;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Mediator.Services.ConcurrencyLimiter;

/// <summary>
/// Default <see cref="IConcurrencyLimiter"/>, registered as a singleton by <c>AddMediator</c>.
/// Enforces per-train, per-principal and global limits on RUN executions with
/// <see cref="SemaphoreSlim"/> instances keyed by train interface FullName and principal id.
/// Infrastructure used by the train execution service; not intended to be called directly.
/// </summary>
/// <remarks>
/// A per-train limit is resolved once per train, on first use: a builder override first, then
/// <c>[TraxConcurrencyLimit]</c>. One semaphore is kept per distinct principal id for the life of
/// the limiter and is never evicted.
/// </remarks>
public class ConcurrencyLimiter : IConcurrencyLimiter
{
    private readonly MediatorConfiguration _configuration;
    private readonly ITrainDiscoveryService _discoveryService;
    private readonly ICurrentPrincipalProvider _principalProvider;
    private readonly ConcurrentDictionary<string, Lazy<SemaphoreSlim?>> _perTrainSemaphores = new();
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _perPrincipalSemaphores = new();
    private readonly SemaphoreSlim? _globalSemaphore;

    /// <summary>
    /// Creates the limiter. The global semaphore is created here when
    /// <see cref="MediatorConfiguration.GlobalMaxConcurrentRun"/> is set; per-train and
    /// per-principal ones are created on first use.
    /// </summary>
    /// <param name="configuration">Supplies the global, per-principal and per-train limits.</param>
    /// <param name="discoveryService">Looks up a train's <c>[TraxConcurrencyLimit]</c>.</param>
    /// <param name="principalProvider">Identifies the caller for the per-principal limit.</param>
    public ConcurrencyLimiter(
        MediatorConfiguration configuration,
        ITrainDiscoveryService discoveryService,
        ICurrentPrincipalProvider principalProvider
    )
    {
        _configuration = configuration;
        _discoveryService = discoveryService;
        _principalProvider = principalProvider;
        _globalSemaphore = configuration.GlobalMaxConcurrentRun is { } globalLimit
            ? new SemaphoreSlim(globalLimit, globalLimit)
            : null;
    }

    /// <inheritdoc/>
    public async Task<IDisposable> AcquireAsync(string trainFullName, CancellationToken ct)
    {
        var perTrainSemaphore = GetOrCreatePerTrainSemaphore(trainFullName);
        var perPrincipalSemaphore = GetOrCreatePerPrincipalSemaphore();

        // Acquire in a deterministic order (per-train → per-principal → global)
        // to prevent cross-lock deadlocks. Release in reverse.
        if (perTrainSemaphore is not null)
            await perTrainSemaphore.WaitAsync(ct);

        try
        {
            if (perPrincipalSemaphore is not null)
                await perPrincipalSemaphore.WaitAsync(ct);
        }
        catch
        {
            perTrainSemaphore?.Release();
            throw;
        }

        try
        {
            if (_globalSemaphore is not null)
                await _globalSemaphore.WaitAsync(ct);
        }
        catch
        {
            perPrincipalSemaphore?.Release();
            perTrainSemaphore?.Release();
            throw;
        }

        return new ConcurrencyPermit(perTrainSemaphore, perPrincipalSemaphore, _globalSemaphore);
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

    private SemaphoreSlim? GetOrCreatePerPrincipalSemaphore()
    {
        if (_configuration.PerPrincipalMaxConcurrentRun is not { } limit)
            return null;

        var principalId = _principalProvider.GetCurrentPrincipalId();
        if (string.IsNullOrEmpty(principalId))
            return null;

        return _perPrincipalSemaphores.GetOrAdd(principalId, _ => new SemaphoreSlim(limit, limit));
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

    private sealed class ConcurrencyPermit(
        SemaphoreSlim? perTrain,
        SemaphoreSlim? perPrincipal,
        SemaphoreSlim? global
    ) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) != 0)
                return;

            // Release in reverse order of acquisition
            global?.Release();
            perPrincipal?.Release();
            perTrain?.Release();
        }
    }
}
