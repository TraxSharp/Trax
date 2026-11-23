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
/// One open watch on <see cref="SnapshotWatchHub"/>. Read <see cref="Changes"/> until it completes; dispose it to
/// stop watching.
/// </summary>
internal sealed class SnapshotWatch : IAsyncDisposable, IDisposable
{
    private readonly SnapshotWatchHub _hub;
    private readonly Channel<SnapshotRowChange> _buffer;

    internal SnapshotWatch(SnapshotWatchHub hub, long? rowId, string? machine)
    {
        _hub = hub;
        RowId = rowId;
        Machine = machine;
        _buffer = rowId is null
            ? Channel.CreateBounded<SnapshotRowChange>(
                new BoundedChannelOptions(SnapshotWatchHub.AllCapacity)
                {
                    SingleReader = true,
                    FullMode = BoundedChannelFullMode.Wait,
                }
            )
            // A row watch keeps only the latest change: each change is the whole row, so a skipped revision
            // loses nothing, and a terminal change replaces whatever was waiting.
            : Channel.CreateBounded<SnapshotRowChange>(
                new BoundedChannelOptions(1)
                {
                    SingleReader = true,
                    FullMode = BoundedChannelFullMode.DropOldest,
                }
            );
    }

    /// <summary>The row watched, or null for a watch on every row.</summary>
    public long? RowId { get; }

    /// <summary>The machine a watch on every row is limited to, or null.</summary>
    public string? Machine { get; }

    /// <summary>The changes, until the watch ends.</summary>
    public ChannelReader<SnapshotRowChange> Changes => _buffer.Reader;

    // The highest revision handed to this watch. Guarded by the hub's gate.
    internal long Delivered { get; set; }

    // Whether the hub still counts this watch. Guarded by the hub's gate.
    internal bool Registered { get; set; } = true;

    internal bool Offer(SnapshotRowChange change) => _buffer.Writer.TryWrite(change);

    internal void Complete() => _buffer.Writer.TryComplete();

    public void Dispose() => _hub.Release(this);

    public ValueTask DisposeAsync()
    {
        _hub.Release(this);
        return ValueTask.CompletedTask;
    }
}
