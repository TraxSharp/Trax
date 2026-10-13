using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.SqlDialect;

namespace Trax.Effect.StateMachine.Persistence.Integration.Fixtures;

/// <summary>
/// Where <see cref="PausingSnapshotStore"/> holds the next write: before it reaches the store (after the caller's
/// read), or after the store has written it and before the caller hears back.
/// </summary>
public enum PausePoint
{
    BeforeWrite,
    AfterWrite,
}

/// <summary>
/// The one-shot gate a <see cref="PausingSnapshotStore"/> holds a write at. A test arms it, starts the call, awaits
/// <see cref="Reached"/>, looks at the database, and releases it.
/// </summary>
public sealed class WriteGate
{
    private readonly TaskCompletionSource _reached = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private readonly TaskCompletionSource _released = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private PausePoint? _armed;

    /// <summary>Holds the next write at <paramref name="point"/>.</summary>
    public void Arm(PausePoint point) => _armed = point;

    /// <summary>Completes when a write is held.</summary>
    public Task Reached => _reached.Task;

    /// <summary>Lets the held write go on.</summary>
    public void Release() => _released.TrySetResult();

    internal async Task At(PausePoint point)
    {
        if (_armed != point)
            return;
        _armed = null;
        _reached.TrySetResult();
        await _released.Task;
    }

    /// <summary>Replaces a host's <see cref="ISnapshotStore"/> with one that holds its writes at <paramref name="gate"/>.</summary>
    public static Action<IServiceCollection> Install(WriteGate gate) =>
        services =>
            services.AddScoped<ISnapshotStore>(sp => new PausingSnapshotStore(
                new EfSnapshotStore(
                    sp.GetRequiredService<IDataContext>(),
                    sp.GetService<ISqlDialect>()
                ),
                gate
            ));
}

/// <summary>
/// A test-only decorator over <see cref="ISnapshotStore"/> that holds a write between the caller's read and its
/// update (<see cref="PausePoint.BeforeWrite"/>), or between the store's write and the caller hearing back
/// (<see cref="PausePoint.AfterWrite"/>), so a test can interleave another reader or writer at that point
/// deterministically. Every member forwards to the inner store, the default ones and the conditional delete included.
/// </summary>
public sealed class PausingSnapshotStore(ISnapshotStore inner, WriteGate gate)
    : ISnapshotStore,
        IConditionalDraftDelete
{
    // The conditional delete a discard makes, held as every other write is. The inner store must have one, so a test
    // never runs the draft service's fallback without saying so.
    Task<bool> IConditionalDraftDelete.DeleteAsRead(
        string userKey,
        string machine,
        Guid id,
        StoredSnapshot read,
        Func<Task> afterDelete
    ) =>
        Write(() =>
            ((IConditionalDraftDelete)inner).DeleteAsRead(userKey, machine, id, read, afterDelete)
        );

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
        Write(() => inner.Delete(userKey, id, cancellationToken));

    public Task Delete(
        string userKey,
        string machine,
        Guid id,
        CancellationToken cancellationToken = default
    ) => Write(() => inner.Delete(userKey, machine, id, cancellationToken));

    public Task<bool> Upsert(
        string userKey,
        Guid id,
        Snapshot snapshot,
        CancellationToken cancellationToken = default
    ) => Write(() => inner.Upsert(userKey, id, snapshot, cancellationToken));

    public Task<bool> Insert(
        string userKey,
        Guid id,
        Snapshot snapshot,
        CancellationToken cancellationToken = default
    ) => Write(() => inner.Insert(userKey, id, snapshot, cancellationToken));

    public Task<bool> Update(
        string userKey,
        Guid id,
        Snapshot snapshot,
        Guid expectedToken,
        string? requestId = null,
        CancellationToken cancellationToken = default
    ) =>
        Write(() =>
            inner.Update(userKey, id, snapshot, expectedToken, requestId, cancellationToken)
        );

    public Task<bool> UpdateWithRequest(
        string userKey,
        Guid id,
        Snapshot snapshot,
        Guid expectedToken,
        AppliedRequest? request,
        CancellationToken cancellationToken = default
    ) =>
        Write(() =>
            inner.UpdateWithRequest(
                userKey,
                id,
                snapshot,
                expectedToken,
                request,
                cancellationToken
            )
        );

    private async Task Write(Func<Task> write)
    {
        await gate.At(PausePoint.BeforeWrite);
        await write();
        await gate.At(PausePoint.AfterWrite);
    }

    private async Task<T> Write<T>(Func<Task<T>> write)
    {
        await gate.At(PausePoint.BeforeWrite);
        var result = await write();
        await gate.At(PausePoint.AfterWrite);
        return result;
    }
}
