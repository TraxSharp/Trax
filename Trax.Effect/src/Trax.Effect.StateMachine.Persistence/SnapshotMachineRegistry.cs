using Microsoft.Extensions.DependencyInjection;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// Resolves the draft service (and exactly-once runner) for a machine by name. Populated once from the
/// discovered <see cref="IMachine"/>s; built per request over the scoped store/claims. This is what lets a
/// single generic mutation serve every registered machine via a <c>machine</c> discriminator.
/// </summary>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/statemachine-api/persistence-ports#isnapshotmachineregistry">SDK reference</seealso>
public interface ISnapshotMachineRegistry
{
    /// <summary>
    /// The draft service for the machine registered as <paramref name="machine"/>, or null if no machine has that
    /// name. Names match case-sensitively.
    /// </summary>
    /// <param name="machine">The machine id (<see cref="IMachine.Name"/>).</param>
    ISnapshotDraftService? Service(string machine);

    /// <summary>
    /// The exactly-once effect runner for the named machine, or null if the name is unknown or the machine binds
    /// no effect.
    /// </summary>
    /// <param name="machine">The machine id (<see cref="IMachine.Name"/>).</param>
    ISnapshotEffectRunner? EffectRunner(string machine);

    /// <summary>The registered machine's schema hash (<see cref="IMachine.SchemaHash"/>), or null if unknown.</summary>
    string? SchemaHash(string machine);
}

/// <summary>
/// The default <see cref="ISnapshotMachineRegistry"/>, registered scoped by <c>AddStateMachines</c> so each
/// request gets services over its own store and claim ledger. It caches one draft service per machine for its
/// lifetime and is not thread-safe. Infrastructure resolved through <see cref="ISnapshotMachineRegistry"/>; not
/// intended to be constructed directly.
/// </summary>
internal sealed class SnapshotMachineRegistry : ISnapshotMachineRegistry
{
    private readonly IReadOnlyDictionary<string, IMachine> _machines;
    private readonly ISnapshotStore _store;
    private readonly IEffectClaimStore _claims;
    private readonly IdempotentEffect _idempotent;
    private readonly IServiceProvider _services;
    private readonly DraftCap? _cap;
    private readonly IInvokedRunCancellation? _runCancellation;
    private readonly Dictionary<string, ISnapshotDraftService> _serviceCache = new(
        StringComparer.Ordinal
    );

    /// <summary>Creates a registry over the discovered machines and this scope's stores.</summary>
    /// <param name="machines">
    /// Every registered machine. The same type registered twice is one machine; two types with the same name throw
    /// <see cref="ArgumentException"/>, which the host's startup check refuses before any request.
    /// </param>
    /// <param name="store">The draft store the services read and write.</param>
    /// <param name="claims">The effect-claim ledger, cleared on reset and used by the effect runners.</param>
    /// <param name="idempotent">The exactly-once primitive handed to effect runners.</param>
    /// <param name="services">The container effect implementations are resolved from.</param>
    /// <param name="options">
    /// Supplies the per-user draft cap and the TTL it counts with. Null (a registry built by hand) applies no cap.
    /// </param>
    /// <param name="runCancellation">Cancels a draft's live invoked run before the draft is deleted.</param>
    /// <exception cref="ArgumentException">Two machine types share a name.</exception>
    public SnapshotMachineRegistry(
        IEnumerable<IMachine> machines,
        ISnapshotStore store,
        IEffectClaimStore claims,
        IdempotentEffect idempotent,
        IServiceProvider services,
        StateMachineOptions? options = null,
        IInvokedRunCancellation? runCancellation = null
    )
    {
        _runCancellation = runCancellation;
        _machines = RegisteredMachines
            .Distinct(machines)
            .ToDictionary(m => m.Name, StringComparer.Ordinal);
        _store = store;
        _claims = claims;
        _idempotent = idempotent;
        _services = services;
        _cap = options?.EffectiveMaxDraftsPerUser is { } max
            ? new DraftCap(max, options.DraftTtl, KeptStates(_machines.Values))
            : null;
    }

    // For a row that does not record whether the server put it in its state, the states the earlier rule kept, as the
    // draft sweep lists them.
    private static IReadOnlyCollection<string> KeptStates(IEnumerable<IMachine> machines) =>
        machines
            .OfType<IMachineInternals>()
            .Where(m => !m.SystemOwned)
            .SelectMany(m =>
                m.KeptFromSweep.Select(state => DraftExpiry.Key(((IMachine)m).Name, state))
            )
            .ToHashSet(StringComparer.Ordinal);

    /// <inheritdoc/>
    public ISnapshotDraftService? Service(string machine)
    {
        if (_serviceCache.TryGetValue(machine, out var cached))
            return cached;
        if (!_machines.TryGetValue(machine, out var found))
            return null;

        // A system-owned machine's instances are created and driven from code only: no user's draft operation
        // reaches it, so it is answered as no machine at all.
        if (found is IMachineInternals { SystemOwned: true })
            return null;

        ISnapshotDraftService service;
        if (found is IMachineInternals internals)
        {
            // A machine that invokes trains reads and expires its drafts through the owner-aware store over the
            // request's data context, and writes them through the outbox, on a context of its own, together with
            // the run's entry, so the snapshot and the run commit in one transaction whatever ISnapshotStore the
            // host registered.
            var invokes = internals.WaitsOnWork
                ? new InvokeRuntime(
                    _services.GetRequiredService<IMachineInstanceStore>(),
                    _services.GetRequiredService<InvokeOutbox>()
                )
                : null;
            var store = invokes?.Store as ISnapshotStore ?? _store;

            // Creating a draft under the cap, and starting one, write to the table through the outbox's own
            // transaction, so they are offered only over the Trax store, never a host's substitute for it.
            var creates = store is EfSnapshotStore ? _services.GetService<InvokeOutbox>() : null;
            service = internals.CreateService(
                store,
                _claims,
                _runCancellation,
                invokes,
                creates,
                creates is null ? null : _cap
            );
        }
        else
            service = found.CreateService(_store, _claims);
        if (service is IEffectRunnerSlot slot)
            slot.EffectRunner = () => EffectRunner(machine);
        // A machine another machine's state may spawn tells its parent when an advance ends it.
        if (
            found is IMachineInternals { Ends.Count: > 0 } child
            && service is IEndNotifierSlot endSlot
        )
        {
            endSlot.EndStates = child.Ends.Keys.ToHashSet(StringComparer.Ordinal);
            endSlot.EndNotifier = (userKey, id, cancellationToken) =>
                SpawnedEnds.Notify(
                    _services.GetRequiredService<IServiceScopeFactory>(),
                    DraftOwner.User(userKey),
                    machine,
                    id,
                    cancellationToken
                );
        }
        _serviceCache[machine] = service;
        return service;
    }

    /// <inheritdoc/>
    public ISnapshotEffectRunner? EffectRunner(string machine)
    {
        if (
            !_machines.TryGetValue(machine, out var found)
            || !found.HasEffect
            || found is IMachineInternals { SystemOwned: true }
        )
            return null;

        return found.CreateEffectRunner(Service(machine)!, _idempotent, _services);
    }

    /// <inheritdoc/>
    public string? SchemaHash(string machine) =>
        _machines.TryGetValue(machine, out var found) ? found.SchemaHash : null;
}
