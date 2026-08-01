namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// Resolves the draft service (and exactly-once runner) for a machine by name. Populated once from the
/// discovered <see cref="IMachine"/>s; built per request over the scoped store/claims. This is what lets a
/// single generic mutation serve every registered machine via a <c>machine</c> discriminator.
/// </summary>
public interface ISnapshotMachineRegistry
{
    ISnapshotDraftService? Service(string machine);

    ISnapshotEffectRunner? EffectRunner(string machine);
}

public sealed class SnapshotMachineRegistry : ISnapshotMachineRegistry
{
    private readonly IReadOnlyDictionary<string, IMachine> _machines;
    private readonly ISnapshotStore _store;
    private readonly IEffectClaimStore _claims;
    private readonly IdempotentEffect _idempotent;
    private readonly IServiceProvider _services;
    private readonly TimeSpan? _draftTtl;
    private readonly Dictionary<string, ISnapshotDraftService> _serviceCache = new(
        StringComparer.Ordinal
    );

    public SnapshotMachineRegistry(
        IEnumerable<IMachine> machines,
        ISnapshotStore store,
        IEffectClaimStore claims,
        IdempotentEffect idempotent,
        IServiceProvider services,
        StateMachineOptions? options = null
    )
    {
        _machines = machines.ToDictionary(m => m.Name, StringComparer.Ordinal);
        _store = store;
        _claims = claims;
        _idempotent = idempotent;
        _services = services;
        _draftTtl = options?.DraftTtl;
    }

    public ISnapshotDraftService? Service(string machine)
    {
        if (_serviceCache.TryGetValue(machine, out var cached))
            return cached;
        if (!_machines.TryGetValue(machine, out var found))
            return null;

        var service = found.CreateService(_store, _claims, _draftTtl);
        _serviceCache[machine] = service;
        return service;
    }

    public ISnapshotEffectRunner? EffectRunner(string machine)
    {
        if (!_machines.TryGetValue(machine, out var found) || !found.HasEffect)
            return null;

        return found.CreateEffectRunner(Service(machine)!, _idempotent, _services);
    }
}
