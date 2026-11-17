using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Extensions;
using Trax.StateMachine;
using Trax.StateMachine.Persistence;

namespace Trax.Scheduler.Tests.Integration.Fakes.Timers;

/// <summary>
/// A test-only decorator over <see cref="ISnapshotStore"/> that, while its <see cref="PauseGate"/> is armed, holds
/// the next write between the caller's read and its update. Every read and write is otherwise the inner store's.
/// </summary>
internal sealed class PausingSnapshotStore(ISnapshotStore inner, PauseGate gate) : ISnapshotStore
{
    /// <summary>Replaces the registered store with this decorator around it, held by <paramref name="gate"/>.</summary>
    public static void Install(IServiceCollection services, PauseGate gate)
    {
        var registered = services.Last(d => d.ServiceType == typeof(ISnapshotStore));
        services.Remove(registered);
        var create = registered.ImplementationFactory.NotNull();
        services.AddScoped<ISnapshotStore>(sp => new PausingSnapshotStore(
            (ISnapshotStore)create(sp),
            gate
        ));
    }

    public Task<StoredSnapshot?> Get(
        string userKey,
        Guid id,
        CancellationToken cancellationToken = default
    ) => inner.Get(userKey, id, cancellationToken);

    public Task<StoredSnapshot?> Get(
        string userKey,
        string machine,
        Guid id,
        CancellationToken cancellationToken = default
    ) => inner.Get(userKey, machine, id, cancellationToken);

    public Task Delete(string userKey, Guid id, CancellationToken cancellationToken = default) =>
        inner.Delete(userKey, id, cancellationToken);

    public Task Delete(
        string userKey,
        string machine,
        Guid id,
        CancellationToken cancellationToken = default
    ) => inner.Delete(userKey, machine, id, cancellationToken);

    public async Task<bool> Upsert(
        string userKey,
        Guid id,
        Snapshot snapshot,
        CancellationToken cancellationToken = default
    )
    {
        await gate.HoldIfArmed().ConfigureAwait(false);
        return await inner.Upsert(userKey, id, snapshot, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> Insert(
        string userKey,
        Guid id,
        Snapshot snapshot,
        CancellationToken cancellationToken = default
    )
    {
        await gate.HoldIfArmed().ConfigureAwait(false);
        return await inner.Insert(userKey, id, snapshot, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> Update(
        string userKey,
        Guid id,
        Snapshot snapshot,
        Guid expectedToken,
        string? requestId = null,
        CancellationToken cancellationToken = default
    )
    {
        await gate.HoldIfArmed().ConfigureAwait(false);
        return await inner
            .Update(userKey, id, snapshot, expectedToken, requestId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<bool> UpdateWithRequest(
        string userKey,
        Guid id,
        Snapshot snapshot,
        Guid expectedToken,
        AppliedRequest? request,
        CancellationToken cancellationToken = default
    )
    {
        await gate.HoldIfArmed().ConfigureAwait(false);
        return await inner
            .UpdateWithRequest(userKey, id, snapshot, expectedToken, request, cancellationToken)
            .ConfigureAwait(false);
    }
}
