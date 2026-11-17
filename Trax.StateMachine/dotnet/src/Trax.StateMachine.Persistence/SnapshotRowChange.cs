using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;

namespace Trax.StateMachine.Persistence;

/// <summary>
/// One row as the hub re-read it. <see cref="Context"/> is the row's context as JSON text, set only on
/// <see cref="SnapshotRowChangeKind.Changed"/>; the identifying fields are set whenever the hub read the row.
/// </summary>
internal sealed record SnapshotRowChange(
    SnapshotRowChangeKind Kind,
    long RowId,
    long Revision,
    string? Machine,
    Guid? Id,
    SnapshotOwnerKind? OwnerKind,
    string? UserKey,
    int Version,
    string? State,
    string? Context,
    string? IrHash,
    DateTimeOffset? UpdatedAt
)
{
    /// <summary>Whether the row is gone.</summary>
    public bool Deleted => Kind is SnapshotRowChangeKind.Gone;

    /// <summary>Whether the row's state waits on a train run it invoked (it holds an invoke token).</summary>
    public bool HasInvokeToken { get; init; }

    /// <summary>When the row was created, where the store recorded it.</summary>
    public DateTimeOffset? CreatedAt { get; init; }

    /// <summary>
    /// For <see cref="SnapshotRowChangeKind.Changed"/>: when the timer of the row's state fires, read with the row, or
    /// null when that state has none. It travels beside the snapshot, never inside it.
    /// </summary>
    public DateTimeOffset? DueAt { get; init; }

    /// <summary>
    /// For <see cref="SnapshotRowChangeKind.Unreadable"/>: why this host cannot read the row, a rehydration error code
    /// (unknown machine, unknown state, a context that is not a JSON object).
    /// </summary>
    public RefusalCode? UnreadableReason { get; init; }

    internal static readonly SnapshotRowChange ReloadAll = new(
        SnapshotRowChangeKind.Reload,
        0,
        0,
        Machine: null,
        Id: null,
        OwnerKind: null,
        UserKey: null,
        0,
        State: null,
        Context: null,
        IrHash: null,
        UpdatedAt: null
    );

    internal static SnapshotRowChange GoneRow(long rowId, string? machine) =>
        new(
            SnapshotRowChangeKind.Gone,
            rowId,
            0,
            machine,
            Id: null,
            OwnerKind: null,
            UserKey: null,
            0,
            State: null,
            Context: null,
            IrHash: null,
            UpdatedAt: null
        );

    internal static SnapshotRowChange UnreadableRow(
        long rowId,
        long revision,
        string machine,
        Guid id,
        SnapshotOwnerKind ownerKind,
        string? userKey,
        string? irHash
    ) =>
        new(
            SnapshotRowChangeKind.Unreadable,
            rowId,
            revision,
            machine,
            id,
            ownerKind,
            userKey,
            0,
            State: null,
            Context: null,
            irHash,
            UpdatedAt: null
        );
}
