using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.SqlDialect;

namespace Trax.StateMachine.Persistence.Integration.Fixtures;

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
        await gate.At(PausePoint.BeforeWrite).ConfigureAwait(false);
        await write().ConfigureAwait(false);
        await gate.At(PausePoint.AfterWrite).ConfigureAwait(false);
    }

    private async Task<T> Write<T>(Func<Task<T>> write)
    {
        await gate.At(PausePoint.BeforeWrite).ConfigureAwait(false);
        var result = await write().ConfigureAwait(false);
        await gate.At(PausePoint.AfterWrite).ConfigureAwait(false);
        return result;
    }
}
