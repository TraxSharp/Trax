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
/// <c>[TraxConcurrencyLimit]</c>. A principal's semaphore exists only while a run for that
/// principal holds or waits for a slot: the last one to leave removes it, so the limiter's memory
/// follows the principals running now rather than every principal it has ever seen.
/// </remarks>
public class ConcurrencyLimiter : IConcurrencyLimiter
{
    private readonly MediatorConfiguration _configuration;
    private readonly ITrainDiscoveryService _discoveryService;
    private readonly ICurrentPrincipalProvider _principalProvider;
    private readonly ConcurrentDictionary<string, Lazy<SemaphoreSlim?>> _perTrainSemaphores = new();
    private readonly Dictionary<string, PrincipalSlot> _perPrincipalSlots = new(
        StringComparer.Ordinal
    );
    private readonly object _perPrincipalLock = new();
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

        // Acquire in a deterministic order (per-train → per-principal → global)
        // to prevent cross-lock deadlocks. Release in reverse.
        if (perTrainSemaphore is not null)
            await perTrainSemaphore.WaitAsync(ct);

        // Rented after the per-train wait, so a run queued behind the per-train limit does not
        // keep its principal's entry alive while it waits there.
        var perPrincipalSlot = RentPerPrincipalSlot();

        try
        {
            if (perPrincipalSlot is not null)
                await perPrincipalSlot.Semaphore.WaitAsync(ct);
        }
        catch
        {
            ReturnPerPrincipalSlot(perPrincipalSlot);
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
            perPrincipalSlot?.Semaphore.Release();
            ReturnPerPrincipalSlot(perPrincipalSlot);
            perTrainSemaphore?.Release();
            throw;
        }

        return new ConcurrencyPermit(this, perTrainSemaphore, perPrincipalSlot, _globalSemaphore);
    }

    /// <summary>
    /// How many principals currently have a semaphore: those with a run holding or waiting for a
    /// slot. Zero when nothing is running.
    /// </summary>
    internal int PerPrincipalEntryCount
    {
        get
        {
            lock (_perPrincipalLock)
                return _perPrincipalSlots.Count;
        }
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

    /// <summary>
    /// Returns the current principal's slot, counting this run in, or null when there is no
    /// per-principal limit or no principal. Every non-null result is handed back exactly once
    /// through <see cref="ReturnPerPrincipalSlot"/>.
    /// </summary>
    private PrincipalSlot? RentPerPrincipalSlot()
    {
        if (_configuration.PerPrincipalMaxConcurrentRun is not { } limit)
            return null;

        var principalId = _principalProvider.GetCurrentPrincipalId();
        if (string.IsNullOrEmpty(principalId))
            return null;

        lock (_perPrincipalLock)
        {
            if (!_perPrincipalSlots.TryGetValue(principalId, out var slot))
            {
                slot = new PrincipalSlot(principalId, limit);
                _perPrincipalSlots.Add(principalId, slot);
            }

            slot.Users++;
            return slot;
        }
    }

    /// <summary>
    /// Counts a run out of its principal's slot, and removes the slot when it was the last run
    /// holding or waiting for it. Nobody can be waiting on a removed semaphore, so the next run
    /// for that principal starts a fresh one with the full limit.
    /// </summary>
    private void ReturnPerPrincipalSlot(PrincipalSlot? slot)
    {
        if (slot is null)
            return;

        lock (_perPrincipalLock)
        {
            if (--slot.Users == 0)
                _perPrincipalSlots.Remove(slot.PrincipalId);
        }
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

    /// <summary>
    /// One principal's semaphore and the number of runs holding or waiting for it. The count is
    /// guarded by the limiter's per-principal lock.
    /// </summary>
    private sealed class PrincipalSlot(string principalId, int limit)
    {
        public string PrincipalId { get; } = principalId;

        public SemaphoreSlim Semaphore { get; } = new(limit, limit);

        public int Users { get; set; }
    }

    private sealed class ConcurrencyPermit(
        ConcurrencyLimiter limiter,
        SemaphoreSlim? perTrain,
        PrincipalSlot? perPrincipal,
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
            perPrincipal?.Semaphore.Release();
            limiter.ReturnPerPrincipalSlot(perPrincipal);
            perTrain?.Release();
        }
    }
}
