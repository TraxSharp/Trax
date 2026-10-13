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

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// The per-host seam between the change notices and whatever pushes instances to clients: <c>machineChanged</c> and
/// the operator feed in Trax.Api and the operations service. A consumer watches a row by its <c>row_id</c> (the
/// incarnation it verified the caller may read), or every row of a machine, and reads
/// <see cref="SnapshotRowChange"/>s; the hub turns the listener's hints into re-read rows.
/// </summary>
/// <remarks>
/// <para><b>What it reads, and how often.</b> A hint for a row nobody here watches reads nothing. Hints are taken in
/// bursts: everything the listener has handed over since the last read is deduplicated and read in one query, so N
/// watchers of one row and M hints for it cost one read, coalesced to the row's latest revision, plus at most one
/// more for a hint that lands while that read runs. Before it reads, the hub waits for the hints to stop arriving
/// (<see cref="CoalesceWindow"/> at a time, at most <see cref="CoalesceLimit"/>), so a burst sent in quick
/// succession is one burst. A <see cref="SnapshotChange.Reload"/> re-reads every watched
/// row.</para>
/// <para><b>What it hands on.</b> Each watcher of a row gets the row's current state first (the first read after
/// it registers, or the last one read for a row already watched), then each newer revision, never one at or below
/// what it has. A re-read that finds the revision unchanged (a forged or repeated hint) sends nothing. A row that is
/// gone ends its watchers with <see cref="SnapshotRowChangeKind.Gone"/>, read from the database and never from the
/// hint. A row this host cannot read (a machine it does not register, a state its machine does not have, a context
/// that is not a JSON object) ends that row's watchers with <see cref="SnapshotRowChangeKind.Unreadable"/>; every
/// other watch goes on. A watcher's buffer holds the latest change only: a slow consumer skips revisions, which is
/// harmless because each change is the whole row.</para>
/// <para><b>Watching every row</b> (<see cref="WatchAll"/>, for the operator feed) gets each re-read row whose
/// revision moved, a <see cref="SnapshotRowChangeKind.Gone"/> for each deleted row (carrying its machine, id, owner
/// kind and last state when the hub had read it, so a watch on one machine can place it; a row it never read is
/// handed to every such watch with its machine null), and a <see cref="SnapshotRowChangeKind.Reload"/> when changes may have been missed. Its buffer
/// is bounded; a consumer that falls behind it is ended, never left silently stale.</para>
/// <para><b>Authorization is the consumer's.</b> The hub knows nothing of callers. A consumer checks the read rule
/// once, then watches the row id it checked, so a re-created row under the same id (a new <c>row_id</c>) never
/// reaches a stream opened on the old one. A change carries the row's context and owner; a consumer that shows
/// neither (the operator feed) drops them.</para>
/// <para>Started by the first watch; stopped with the host. It reads the listener only while a watch is open, telling
/// it which rows it watches (every row of the watched machines as well while a <see cref="WatchAll"/> is open), so a polling listener reads only those
/// and stops when the last watch ends; the next watch starts it again. <see cref="ActiveWatches"/> counts the open
/// watches, and each is released by disposing it or when the hub ends it.</para>
/// </remarks>
internal sealed class SnapshotWatchHub(
    ISnapshotChangeListener listener,
    IDataContextProviderFactory contexts,
    IEnumerable<IMachine> machines,
    ILogger<SnapshotWatchHub>? logger = null
) : IHostedService, IAsyncDisposable, IDisposable
{
    /// <summary>How many changes a <see cref="WatchAll"/> buffer holds before the watch is ended.</summary>
    internal const int AllCapacity = 4096;

    /// <summary>How many row ids one read asks for at most.</summary>
    internal const int ReadBatch = 500;

    /// <summary>How many rows' last revisions the hub remembers for <see cref="WatchAll"/> watchers.</summary>
    internal const int RememberedRevisions = 10_000;

    /// <summary>The wait before a read that failed is tried again.</summary>
    internal static TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How long the hub waits for hints to stop arriving before it reads: a burst of hints that arrive this close
    /// together is read once. Settable for tests.
    /// </summary>
    internal static TimeSpan CoalesceWindow { get; set; } = TimeSpan.FromMilliseconds(10);

    /// <summary>The longest the hub waits for a burst to end before it reads anyway.</summary>
    internal static TimeSpan CoalesceLimit { get; set; } = TimeSpan.FromMilliseconds(100);

    private readonly ILogger _logger = logger ?? NullLogger<SnapshotWatchHub>.Instance;
    private readonly object _gate = new();
    private readonly Dictionary<long, WatchedRow> _rows = [];
    private readonly List<SnapshotWatch> _all = [];

    // The last change each row was seen at by a watch on every row, without its context and owner: its revision, so
    // a re-read that moved nothing is not sent again, and what identifies it, so its delete can be placed.
    private readonly Dictionary<long, SnapshotRowChange> _seen = [];
    private readonly HashSet<long> _pending = [];
    private readonly Channel<bool> _wake = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite }
    );
    private readonly CancellationTokenSource _stopping = new();

    // The current reading of the listener: open while any watch is, cancelled when the last one ends.
    private CancellationTokenSource? _session;
    private Task? _hearing;
    private readonly Lazy<IReadOnlyDictionary<string, HashSet<string>?>> _states = new(() =>
        machines
            .GroupBy(m => m.Name, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => StatesOf(g.First()), StringComparer.Ordinal)
    );
    private Task? _loop;
    private bool _listening;
    private bool _reloadAll;
    private bool _stopped;
    private int _activeWatches;

    // Hints taken into the pending set so far, so the reader can tell a burst that is still arriving. Guarded by the
    // gate.
    private long _hints;

    /// <summary>The watches open now, of either kind.</summary>
    public int ActiveWatches => Volatile.Read(ref _activeWatches);

    /// <summary>
    /// Watches the row <paramref name="rowId"/>. The first change is the row as it is now (or
    /// <see cref="SnapshotRowChangeKind.Gone"/> when there is none), then each newer revision until the row is gone,
    /// cannot be read, the host stops, or the watch is disposed.
    /// </summary>
    public SnapshotWatch Watch(long rowId)
    {
        var watch = new SnapshotWatch(this, rowId, machine: null);
        lock (_gate)
        {
            if (_stopped)
            {
                watch.Complete();
                return watch;
            }
            Interlocked.Increment(ref _activeWatches);
            if (!_rows.TryGetValue(rowId, out var row))
                _rows[rowId] = row = new WatchedRow();
            row.Watchers.Add(watch);
            if (row.Latest is { } latest)
            {
                watch.Offer(latest);
                watch.Delivered = latest.Revision;
            }
            else
                _pending.Add(rowId);
            Start();
        }
        _wake.Writer.TryWrite(true);
        return watch;
    }

    /// <summary>
    /// Watches every row, or every row of <paramref name="machine"/>: each write as it is re-read, each delete, and a
    /// <see cref="SnapshotRowChangeKind.Reload"/> first and whenever changes may have been missed.
    /// </summary>
    public SnapshotWatch WatchAll(string? machine)
    {
        var watch = new SnapshotWatch(this, rowId: null, machine);
        lock (_gate)
        {
            if (_stopped)
            {
                watch.Complete();
                return watch;
            }
            Interlocked.Increment(ref _activeWatches);
            _all.Add(watch);
            if (_listening)
                watch.Offer(SnapshotRowChange.ReloadAll);
            Start();
        }
        return watch;
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task? loop,
            hearing;
        lock (_gate)
        {
            _stopped = true;
            loop = _loop;
            hearing = _hearing;
        }
        await _stopping.CancelAsync();
        if (loop is not null)
            await loop.WaitAsync(cancellationToken).ConfigureAwait(false);
        if (hearing is not null)
            await hearing.WaitAsync(cancellationToken).ConfigureAwait(false);
        EndAll();
    }

    // The container may dispose this more than once (it is registered under several service types), so disposing is
    // stopping, which is idempotent; the token source has no timer, so it is left to the collector.
    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None);

    public void Dispose()
    {
        lock (_gate)
            _stopped = true;
        _stopping.Cancel();
        EndAll();
    }

    // Called by a watch being disposed: forgets it, and the row when it was its last watcher.
    internal void Release(SnapshotWatch watch)
    {
        lock (_gate)
        {
            if (!watch.Registered)
                return;
            watch.Registered = false;
            Interlocked.Decrement(ref _activeWatches);
            if (watch.RowId is { } rowId)
            {
                if (_rows.TryGetValue(rowId, out var row))
                {
                    row.Watchers.Remove(watch);
                    if (row.Watchers.Count == 0)
                        _rows.Remove(rowId);
                }
            }
            else
                _all.Remove(watch);
            // Nothing is watched: stop reading the listener, so a polling one stops polling. What was pending is
            // dropped with the watches it was for, and the next watch reads from a new session's reload.
            if (_rows.Count == 0 && _all.Count == 0 && _session is { } session)
            {
                session.Cancel();
                _session = null;
                _listening = false;
                _pending.Clear();
            }
        }
        watch.Complete();
    }

    // Called under the gate.
    private void Start()
    {
        _loop ??= Task.Run(() => Read(_stopping.Token));
        if (_session is null)
        {
            var session = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
            _session = session;
            _hearing = Task.Run(() => Hear(session.Token));
        }
    }

    // What the hub watches, for a listener that polls: the rows watched one by one, and every row as well while a
    // watch on every row is open, of the machines those watches are limited to (every machine when one is not). The
    // rows are named even then, so a row watched and deleted between two polls of every row, which neither poll read,
    // still ends its watchers with gone.
    private SnapshotWatchScope WatchedRows()
    {
        lock (_gate)
            return new SnapshotWatchScope(
                [.. _rows.Keys],
                EveryRow: _all.Count > 0,
                Machines: _all.Count == 0 || _all.Any(w => w.Machine is null)
                    ? null
                    : _all.Select(w => w.Machine!).ToHashSet(StringComparer.Ordinal)
            );
    }

    // Takes the listener's hints into the pending set: a hint for a row nobody here watches is dropped at once. Runs
    // for one session, until the last watch ends or the host stops.
    private async Task Hear(CancellationToken sessionToken)
    {
        try
        {
            await foreach (var change in listener.ChangesAsync(sessionToken, WatchedRows))
            {
                lock (_gate)
                {
                    if (sessionToken.IsCancellationRequested)
                        break;
                    switch (change)
                    {
                        case SnapshotChange.Reload:
                            _listening = true;
                            _reloadAll = true;
                            foreach (var watch in _all)
                                watch.Offer(SnapshotRowChange.ReloadAll);
                            break;
                        case SnapshotChange.Row(var rowId):
                            if (!_rows.ContainsKey(rowId) && _all.Count == 0)
                                continue;
                            _pending.Add(rowId);
                            _hints++;
                            break;
                    }
                }
                _wake.Writer.TryWrite(true);
            }
        }
        catch (OperationCanceledException) when (sessionToken.IsCancellationRequested)
        {
            // The last watch ended, or the host is stopping.
        }
    }

    // Reads each burst of pending rows in one query, once the listener is listening (so nothing written after the
    // read can be missed), and hands the result to the watchers.
    private async Task Read(CancellationToken stoppingToken)
    {
        try
        {
            while (await _wake.Reader.WaitToReadAsync(stoppingToken))
            {
                _wake.Reader.TryRead(out _);
                await Settle(stoppingToken);
                List<long> burst;
                lock (_gate)
                {
                    if (!_listening)
                        continue;
                    if (_reloadAll)
                    {
                        _pending.UnionWith(_rows.Keys);
                        _reloadAll = false;
                    }
                    burst = [.. _pending];
                    _pending.Clear();
                }
                if (burst.Count == 0)
                    continue;

                try
                {
                    var read = await ReadRows(burst, stoppingToken);
                    lock (_gate)
                        foreach (var rowId in burst)
                            Deliver(rowId, read.GetValueOrDefault(rowId));
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(
                        ex,
                        "Could not re-read {Count} changed state-machine rows; trying again in {Delay}.",
                        burst.Count,
                        RetryDelay
                    );
                    lock (_gate)
                        _pending.UnionWith(burst);
                    await Task.Delay(RetryDelay, stoppingToken);
                    _wake.Writer.TryWrite(true);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Stopping.
        }
    }

    // Waits while hints keep arriving, a window at a time and at most the limit, so a burst of notices for one row
    // (a writer's quick succession of events, or a flood of forged ones) costs one read of it.
    private async Task Settle(CancellationToken stoppingToken)
    {
        var started = Stopwatch.GetTimestamp();
        while (Stopwatch.GetElapsedTime(started) < CoalesceLimit)
        {
            long before;
            lock (_gate)
                before = _hints;
            await Task.Delay(CoalesceWindow, stoppingToken);
            lock (_gate)
                if (_hints == before)
                    return;
        }
    }

    private async Task<Dictionary<long, SnapshotRowChange>> ReadRows(
        List<long> rowIds,
        CancellationToken cancellationToken
    )
    {
        var found = new Dictionary<long, SnapshotRowChange>(rowIds.Count);
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        foreach (var chunk in rowIds.Chunk(ReadBatch))
        {
            var rows = await db
                .SnapshotDrafts.AsNoTracking()
                .Where(d => chunk.Contains(d.RowId))
                .Select(d => new
                {
                    d.RowId,
                    d.Id,
                    d.Machine,
                    d.OwnerKind,
                    d.UserKey,
                    d.Revision,
                    d.Version,
                    d.State,
                    d.Context,
                    d.IrHash,
                    d.UpdatedAt,
                    d.CreatedAt,
                    d.DueAt,
                    HasInvokeToken = d.InvokeToken != null,
                })
                .ToListAsync(cancellationToken);
            foreach (var row in rows)
                found[row.RowId] = Unreadable(row.Machine, row.State, row.Context) is { } reason
                    ? SnapshotRowChange.UnreadableRow(
                        row.RowId,
                        row.Revision,
                        row.Machine,
                        row.Id,
                        row.OwnerKind,
                        row.UserKey,
                        row.IrHash
                    ) with
                    {
                        Version = row.Version,
                        State = row.State,
                        UnreadableReason = reason,
                        HasInvokeToken = row.HasInvokeToken,
                        CreatedAt = row.CreatedAt,
                        UpdatedAt = row.UpdatedAt,
                    }
                    : new SnapshotRowChange(
                        SnapshotRowChangeKind.Changed,
                        row.RowId,
                        row.Revision,
                        row.Machine,
                        row.Id,
                        row.OwnerKind,
                        row.UserKey,
                        row.Version,
                        row.State,
                        row.Context,
                        row.IrHash,
                        row.UpdatedAt
                    )
                    {
                        HasInvokeToken = row.HasInvokeToken,
                        CreatedAt = row.CreatedAt,
                        DueAt = row.DueAt,
                    };
        }
        return found;
    }

    // Under the gate: hands one re-read row (null when it is gone) to the watchers that have not seen it.
    private void Deliver(long rowId, SnapshotRowChange? read)
    {
        var change = read ?? Gone(rowId);
        DeliverToAll(change);

        if (!_rows.TryGetValue(rowId, out var row))
            return;

        if (change.Kind is SnapshotRowChangeKind.Changed)
        {
            row.Latest = change;
            foreach (var watch in row.Watchers)
                if (change.Revision > watch.Delivered)
                {
                    watch.Offer(change);
                    watch.Delivered = change.Revision;
                }
            return;
        }

        // Gone or unreadable: the end of every watch on the row.
        _rows.Remove(rowId);
        foreach (var watch in row.Watchers)
        {
            watch.Offer(change);
            watch.Registered = false;
            Interlocked.Decrement(ref _activeWatches);
            watch.Complete();
        }
    }

    // Under the gate: hands a re-read row to the watches on every row, unless they had it at this revision already.
    // The revisions are remembered only while such a watch is open.
    private void DeliverToAll(SnapshotRowChange change)
    {
        if (_all.Count == 0)
        {
            _seen.Clear();
            return;
        }
        if (change.Kind is SnapshotRowChangeKind.Gone)
            _seen.Remove(change.RowId);
        else
        {
            if (_seen.TryGetValue(change.RowId, out var seen) && seen.Revision == change.Revision)
                return;
            if (_seen.Count >= RememberedRevisions)
                _seen.Clear();
            _seen[change.RowId] = change with { Context = null, UserKey = null };
        }

        List<SnapshotWatch>? overflowed = null;
        foreach (var watch in _all)
        {
            if (
                watch.Machine is not null
                && change.Machine is not null
                && !string.Equals(watch.Machine, change.Machine, StringComparison.Ordinal)
            )
                continue;
            if (!watch.Offer(change))
                (overflowed ??= []).Add(watch);
        }
        if (overflowed is null)
            return;
        foreach (var watch in overflowed)
        {
            _all.Remove(watch);
            watch.Registered = false;
            Interlocked.Decrement(ref _activeWatches);
            watch.Complete();
        }
    }

    // The delete of a row, carrying what identifies it (its machine, id, owner kind and last state, never its context
    // or owner) when the hub had read it: a watch on one machine places the delete by it.
    private SnapshotRowChange Gone(long rowId)
    {
        var known =
            _rows.TryGetValue(rowId, out var row) && row.Latest is { } latest ? latest
            : _seen.TryGetValue(rowId, out var seen) ? seen
            : null;
        return known is null
            ? SnapshotRowChange.GoneRow(rowId, null)
            : SnapshotRowChange.GoneRow(rowId, known.Machine) with
            {
                Revision = known.Revision,
                Id = known.Id,
                OwnerKind = known.OwnerKind,
                Version = known.Version,
                State = known.State,
                CreatedAt = known.CreatedAt,
                UpdatedAt = known.UpdatedAt,
            };
    }

    // Why this host cannot read the row as its machine, as a rehydration error code, or null when it can: a machine
    // it registers, a state that machine has, and a context that is a JSON object. A machine whose states cannot be
    // listed is trusted for the state.
    private string? Unreadable(string machine, string state, string context)
    {
        if (!_states.Value.TryGetValue(machine, out var states))
            return RehydrationErrorCodes.UnknownMachine;
        if (states is not null && !states.Contains(state))
            return RehydrationErrorCodes.UnknownState;
        try
        {
            return JsonNode.Parse(context) is JsonObject
                ? null
                : RehydrationErrorCodes.InvalidContext;
        }
        catch (JsonException)
        {
            return RehydrationErrorCodes.InvalidContext;
        }
    }

    private static HashSet<string>? StatesOf(IMachine machine)
    {
        try
        {
            return machine.States.Select(s => s.Name).ToHashSet(StringComparer.Ordinal);
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private void EndAll()
    {
        List<SnapshotWatch> ending;
        lock (_gate)
        {
            ending = [.. _rows.Values.SelectMany(r => r.Watchers), .. _all];
            _rows.Clear();
            _all.Clear();
            foreach (var watch in ending)
            {
                if (watch.Registered)
                    Interlocked.Decrement(ref _activeWatches);
                watch.Registered = false;
            }
        }
        foreach (var watch in ending)
            watch.Complete();
    }

    private sealed class WatchedRow
    {
        public List<SnapshotWatch> Watchers { get; } = [];

        // The last re-read of the row, handed to a watcher that joins later.
        public SnapshotRowChange? Latest { get; set; }
    }
}

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

/// <summary>What a <see cref="SnapshotWatch"/> hands on.</summary>
internal enum SnapshotRowChangeKind
{
    /// <summary>The row as it is now, at a revision newer than the watcher had.</summary>
    Changed,

    /// <summary>The row no longer exists: deleted or expired. The end of a watch on it.</summary>
    Gone,

    /// <summary>This host cannot read the row (written by another version, or by hand). The end of a watch on it.</summary>
    Unreadable,

    /// <summary>For a watch on every row only: changes may have been missed, so list again.</summary>
    Reload,
}

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
    public string? UnreadableReason { get; init; }

    internal static readonly SnapshotRowChange ReloadAll = new(
        SnapshotRowChangeKind.Reload,
        0,
        0,
        null,
        null,
        null,
        null,
        0,
        null,
        null,
        null,
        null
    );

    internal static SnapshotRowChange GoneRow(long rowId, string? machine) =>
        new(
            SnapshotRowChangeKind.Gone,
            rowId,
            0,
            machine,
            null,
            null,
            null,
            0,
            null,
            null,
            null,
            null
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
            null,
            null,
            irHash,
            null
        );
}
