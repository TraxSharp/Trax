using System.Runtime.InteropServices;
using Trax.StateMachine;

namespace Trax.StateMachine.Persistence;

/// <summary>
/// An <see cref="ISnapshotStore"/> whose blind writes report the row they left, so the draft service can answer a
/// save with its revision without a second read, which another write could already have overtaken. Each returns
/// null where the <see cref="ISnapshotStore"/> member it mirrors returns false.
/// </summary>
internal interface IRowReportingSnapshotStore
{
    /// <summary><see cref="ISnapshotStore.Upsert"/>, reporting the row it wrote.</summary>
    Task<WrittenRow?> Upsert(
        string userKey,
        Guid id,
        Snapshot snapshot,
        CancellationToken cancellationToken
    );

    /// <summary><see cref="ISnapshotStore.Insert"/>, reporting the row it created.</summary>
    Task<WrittenRow?> Insert(
        string userKey,
        Guid id,
        Snapshot snapshot,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// <see cref="ISnapshotStore.UpdateWithRequest"/> guarded by the token <paramref name="read"/> was read with,
    /// reporting the row it left: <paramref name="read"/>'s next revision, and the due time the write gave it.
    /// <paramref name="note"/> is what the instance's history records for it; null records a command when
    /// <paramref name="request"/> names one, else a save.
    /// </summary>
    Task<WrittenRow?> UpdateWithRequest(
        string userKey,
        Guid id,
        Snapshot snapshot,
        StoredSnapshot read,
        AppliedRequest? request,
        CancellationToken cancellationToken,
        TransitionNote? note = null
    );
}
