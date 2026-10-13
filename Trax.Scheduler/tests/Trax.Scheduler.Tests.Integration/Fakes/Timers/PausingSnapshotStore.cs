using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.StateMachine;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Scheduler.Tests.Integration.Fakes.Timers;

/// <summary>
/// A test's hold on a <see cref="PausingSnapshotStore"/>: armed, the store's next write waits after the caller's
/// read until the test releases it, so a test can put another write between a client's read and its update.
/// </summary>
public sealed class PauseGate
{
    private TaskCompletionSource _reached = New();
    private TaskCompletionSource _release = New();
    private int _armed;

    /// <summary>Completes once a write has been held.</summary>
    public Task Reached => _reached.Task;

    /// <summary>Holds the next write through the store.</summary>
    public void Arm()
    {
        _reached = New();
        _release = New();
        Volatile.Write(ref _armed, 1);
    }

    /// <summary>Lets the held write go on.</summary>
    public void Release() => _release.TrySetResult();

    /// <summary>Disarms the gate and lets any held write go on.</summary>
    public void Reset()
    {
        Volatile.Write(ref _armed, 0);
        _release.TrySetResult();
    }

    internal async Task HoldIfArmed()
    {
        if (Interlocked.Exchange(ref _armed, 0) == 0)
            return;
        _reached.TrySetResult();
        await _release.Task;
    }

    private static TaskCompletionSource New() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

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
        var create = registered.ImplementationFactory!;
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
        await gate.HoldIfArmed();
        return await inner.Upsert(userKey, id, snapshot, cancellationToken);
    }

    public async Task<bool> Insert(
        string userKey,
        Guid id,
        Snapshot snapshot,
        CancellationToken cancellationToken = default
    )
    {
        await gate.HoldIfArmed();
        return await inner.Insert(userKey, id, snapshot, cancellationToken);
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
        await gate.HoldIfArmed();
        return await inner.Update(
            userKey,
            id,
            snapshot,
            expectedToken,
            requestId,
            cancellationToken
        );
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
        await gate.HoldIfArmed();
        return await inner.UpdateWithRequest(
            userKey,
            id,
            snapshot,
            expectedToken,
            request,
            cancellationToken
        );
    }
}
