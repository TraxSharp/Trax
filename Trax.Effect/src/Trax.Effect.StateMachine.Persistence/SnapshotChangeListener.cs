using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SnapshotNotices;

namespace Trax.Effect.StateMachine.Persistence;

/// <summary>
/// The host's one <see cref="ISnapshotChangeListener"/>. On Postgres it holds one session listening on
/// <see cref="ISnapshotChangeListener.Channel"/> through the provider's <see cref="ISnapshotNoticeSource"/>, and fans
/// each notice out to every reader; on SQLite and InMemory, which have no notifications, and on Postgres with
/// <see cref="StateMachineChangeFeed.Poll"/>, it polls the <c>(row_id, revision)</c> pairs of the rows its readers
/// watch (and every row of the machines a reader watches in full, or of every machine when one does not name its
/// machine) and reports each new revision, delete and re-created row.
/// </summary>
/// <remarks>
/// <para>Started by the first reader, not by the host: a host nothing watches opens no session and polls nothing.
/// On Postgres the session then stays open until the host stops (<see cref="StopAsync"/>) or the container is
/// disposed. Polling stops when its last reader leaves, and the next reader starts it again, with a reload.</para>
/// <para>A lost session is opened again after a back-off that doubles from <see cref="ReconnectDelay"/> up to
/// <see cref="ReconnectDelayCap"/>, and every reader is then told to reload: a write committed while it was away is
/// found by the readers' re-read. A notice whose payload is not a row id is counted on
/// <see cref="StateMachineInstruments.SnapshotNoticeMalformed"/> and dropped. Every <see cref="QueueUsageInterval"/>,
/// busy or idle, the session reads <c>pg_notification_queue_usage()</c> between two notices and reports it on
/// <see cref="StateMachineInstruments.NotificationQueueUsage"/>.</para>
/// <para>Each reader has a buffer of <see cref="ReaderCapacity"/> changes. A reader that falls further behind loses
/// the hints that do not fit and is told to reload instead, so a slow reader never holds the others up or grows
/// without bound.</para>
/// </remarks>
internal sealed class SnapshotChangeListener(
    IDataContextProviderFactory contexts,
    ILogger<SnapshotChangeListener>? logger = null,
    ISnapshotNoticeSource? notices = null
) : ISnapshotChangeListener, IHostedService, IAsyncDisposable, IDisposable
{
    /// <summary>How many changes a reader's buffer holds before it is told to reload instead.</summary>
    internal const int ReaderCapacity = 1024;

    /// <summary>The first wait before a lost session is opened again; it doubles up to the cap.</summary>
    internal static TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>The longest wait before a lost session is opened again.</summary>
    internal static readonly TimeSpan ReconnectDelayCap = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How often the notification queue's usage is read, whether notices are arriving or not. Settable for tests.
    /// </summary>
    internal static TimeSpan QueueUsageInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>How many watched rows one poll reads at most per query.</summary>
    internal const int ReadBatch = 500;

    /// <summary>How often a polling listener polls the table. Settable for tests.</summary>
    internal TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>The rows the last poll read, for tests that check what a poll reads.</summary>
    internal IReadOnlyCollection<long> LastPolled => Volatile.Read(ref _lastPolled);

    private IReadOnlyCollection<long> _lastPolled = [];

    private readonly ILogger _logger = logger ?? NullLogger<SnapshotChangeListener>.Instance;
    private readonly object _gate = new();
    private readonly List<Reader> _readers = [];
    private readonly CancellationTokenSource _stopping = new();
    private CancellationTokenSource? _run;
    private Task? _loop;
    private bool _listening;
    private bool _stopped;

    /// <summary>Whether this listener polls (SQLite, InMemory) rather than listens (Postgres).</summary>
    internal bool Polls => notices is null;

    /// <summary>Whether this listener polls the table now: it does while it has a reader.</summary>
    internal bool IsPolling
    {
        get
        {
            lock (_gate)
                return Polls && _loop is { IsCompleted: false };
        }
    }

    public IAsyncEnumerable<SnapshotChange> ChangesAsync(CancellationToken cancellationToken) =>
        ChangesAsync(cancellationToken, null);

    public async IAsyncEnumerable<SnapshotChange> ChangesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken,
        Func<SnapshotWatchScope?>? watched
    )
    {
        var reader = new Reader(watched);
        if (!Join(reader))
            yield break;

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                _stopping.Token
            );
            while (await reader.TryReadAsync(linked.Token, _stopping.Token) is { } next)
                yield return next;
        }
        finally
        {
            lock (_gate)
            {
                _readers.Remove(reader);
                // Nothing is left to poll for: stop, and let the next reader start again from a new baseline.
                if (Polls && _readers.Count == 0 && _run is { } run)
                {
                    run.Cancel();
                    _run = null;
                    _loop = null;
                    _listening = false;
                }
            }
        }
    }

    // Adds a reader, starting the loop for the first. A reader joining a live session has missed nothing it could
    // have asked for, but it has not read what it watches yet: the first item is always a reload. Before the session
    // is live, the reload comes when it is.
    private bool Join(Reader reader)
    {
        lock (_gate)
        {
            if (_stopped)
                return false;
            _readers.Add(reader);
            if (_listening)
                reader.Write(SnapshotChange.Reload.Instance);
            if (_loop is null)
            {
                _run = CancellationTokenSource.CreateLinkedTokenSource(_stopping.Token);
                var token = _run.Token;
                _loop = Task.Run(() => Run(token));
            }
            return true;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Task? loop;
        lock (_gate)
        {
            _stopped = true;
            loop = _loop;
        }
        await _stopping.CancelAsync();
        if (loop is not null)
            await loop.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    // The container may dispose this more than once (it is registered under several service types), so disposing is
    // stopping, which is idempotent; the token source has no timer, so it is left to the collector.
    public async ValueTask DisposeAsync() => await StopAsync(CancellationToken.None);

    public void Dispose()
    {
        lock (_gate)
            _stopped = true;
        _stopping.Cancel();
        // The loop ends on cancellation; it holds nothing a synchronous dispose must wait for, since the session it
        // opened is disposed by the loop itself and the provider's data source by the container.
    }

    private Task Run(CancellationToken stoppingToken) =>
        notices is null ? Poll(stoppingToken) : Listen(notices, stoppingToken);

    // Holds a subscription for as long as the host runs, opening it again after a lost connection with a back-off
    // that doubles up to its cap. Each (re)opened subscription tells every reader to reload.
    private async Task Listen(ISnapshotNoticeSource source, CancellationToken stoppingToken)
    {
        var delay = ReconnectDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var subscription = await source.SubscribeAsync(stoppingToken);
                StateMachineMeter.ReportQueueUsage(
                    this,
                    await subscription.QueueUsageAsync(stoppingToken)
                );
                var sampled = Stopwatch.GetTimestamp();
                Live();
                delay = ReconnectDelay;
                while (true)
                {
                    // The queue is read on the session between notices, once an interval has passed, so a steady
                    // stream of notices (when the queue is most likely to fill) never stops the reading.
                    // The wait is whole milliseconds, and a session's wait of zero is no time-out at all, so less
                    // than one left is due now.
                    var due = QueueUsageInterval - Stopwatch.GetElapsedTime(sampled);
                    if (due < TimeSpan.FromMilliseconds(1))
                    {
                        StateMachineMeter.ReportQueueUsage(
                            this,
                            await subscription.QueueUsageAsync(stoppingToken)
                        );
                        sampled = Stopwatch.GetTimestamp();
                        due = QueueUsageInterval;
                    }
                    var payload = await subscription.NextAsync(due, stoppingToken);
                    if (payload is null)
                        continue;
                    if (TryParseRowId(payload, out var rowId))
                        Broadcast(new SnapshotChange.Row(rowId));
                    else
                        StateMachineMeter.SnapshotNoticeMalformed.Add(1);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                Lost();
                _logger.LogWarning(
                    ex,
                    "Lost the snapshot change notices; listening again in {Delay}. Watchers re-read their rows when it does.",
                    delay
                );
                if (!await Wait(delay, stoppingToken))
                    break;
                delay = delay * 2 > ReconnectDelayCap ? ReconnectDelayCap : delay * 2;
            }
        }
        Lost();
    }

    // SQLite and InMemory, and Postgres on Poll: compares the (row_id, revision) pairs of the rows the readers watch
    // with the previous poll's; every row of the watched machines as well when a reader watches every row (every
    // machine when one of them does not name its machine, or did not say what it watches). A row that is new or whose
    // revision moved is reported, and so is a gone one the previous poll read in what this poll reads; a row nobody
    // watches any more is forgotten. A row watched one by one that is missing is reported once, whether or not the
    // previous poll saw it, and also while every row is polled: one watched since that poll was read by its reader
    // when the watch began, and a delete before this poll is still a delete to it, though no poll of every row ever
    // read it.
    // The first poll only takes the baseline and tells every reader to reload, as a Postgres session does when it
    // starts listening, and so does a poll that reads every row of a machine the previous one did not: what it did
    // not read before has no baseline to compare with.
    private async Task Poll(CancellationToken stoppingToken)
    {
        Dictionary<long, PolledRow>? known = null;
        HashSet<long> missing = [];
        PollScope? knew = null;
        var delay = ReconnectDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var scope = Watched();
                var current = await ReadRevisions(scope, stoppingToken);
                Volatile.Write(ref _lastPolled, [.. current.Keys]);
                if (known is null || (scope.EveryRow && !(knew?.Covers(scope) ?? false)))
                    Live(stoppingToken);
                else
                {
                    foreach (var (rowId, row) in current)
                        if (
                            !known.TryGetValue(rowId, out var before)
                            || before.Revision != row.Revision
                        )
                            Broadcast(new SnapshotChange.Row(rowId), stoppingToken);
                    var gone = new HashSet<long>();
                    if (scope.EveryRow)
                        foreach (var (rowId, row) in known)
                            if (!current.ContainsKey(rowId) && scope.Reads(row.Machine))
                                gone.Add(rowId);
                    foreach (var rowId in scope.Rows)
                        if (!current.ContainsKey(rowId) && !missing.Contains(rowId))
                            gone.Add(rowId);
                    foreach (var rowId in gone)
                        Broadcast(new SnapshotChange.Row(rowId), stoppingToken);
                }
                missing = [.. scope.Rows.Where(r => !current.ContainsKey(r))];
                known = current;
                knew = scope;
                delay = ReconnectDelay;
                if (!await Wait(PollInterval, stoppingToken))
                    break;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // The baseline is lost with the reads that failed; the next poll that succeeds takes a new one and
                // tells every reader to reload.
                known = null;
                Lost(stoppingToken);
                _logger.LogWarning(
                    ex,
                    "Could not poll the state-machine table for changes; polling again in {Delay}. Watchers re-read their rows when it succeeds.",
                    delay
                );
                if (!await Wait(delay, stoppingToken))
                    break;
                delay = delay * 2 > ReconnectDelayCap ? ReconnectDelayCap : delay * 2;
            }
        }
        Lost(stoppingToken);
    }

    // The rows the readers watch one by one, and whether every row is watched as well (when a reader watches every
    // row or did not say what it watches), of which machines (null: every machine).
    private PollScope Watched()
    {
        Func<SnapshotWatchScope?>?[] sources;
        lock (_gate)
            sources = [.. _readers.Select(r => r.Watched)];
        var rows = new HashSet<long>();
        var everyRow = false;
        var machines = new HashSet<string>(StringComparer.Ordinal);
        var everyMachine = false;
        foreach (var source in sources)
        {
            if (source?.Invoke() is not { } scope)
            {
                everyRow = everyMachine = true;
                continue;
            }
            rows.UnionWith(scope.Rows);
            if (!scope.EveryRow)
                continue;
            everyRow = true;
            if (scope.Machines is null)
                everyMachine = true;
            else
                machines.UnionWith(scope.Machines);
        }
        return new PollScope(rows, everyRow, everyRow && !everyMachine ? machines : null);
    }

    private async Task<Dictionary<long, PolledRow>> ReadRevisions(
        PollScope scope,
        CancellationToken cancellationToken
    )
    {
        await using var db = await contexts.CreateDbContextAsync(cancellationToken);
        var rows = db.SnapshotDrafts.AsNoTracking();
        if (scope.EveryRow && scope.Machines is null)
            return await rows.Select(d => new
                {
                    d.RowId,
                    d.Revision,
                    d.Machine,
                })
                .ToDictionaryAsync(
                    r => r.RowId,
                    r => new PolledRow(r.Revision, r.Machine),
                    cancellationToken
                );

        var revisions = new Dictionary<long, PolledRow>();
        if (scope.EveryRow && scope.Machines is { Count: > 0 } machines)
        {
            string[] names = [.. machines];
            foreach (
                var row in await rows.Where(d => names.Contains(d.Machine))
                    .Select(d => new
                    {
                        d.RowId,
                        d.Revision,
                        d.Machine,
                    })
                    .ToListAsync(cancellationToken)
            )
                revisions[row.RowId] = new PolledRow(row.Revision, row.Machine);
        }
        foreach (var batch in scope.Rows.Where(r => !revisions.ContainsKey(r)).Chunk(ReadBatch))
        foreach (
            var row in await rows.Where(d => batch.Contains(d.RowId))
                .Select(d => new
                {
                    d.RowId,
                    d.Revision,
                    d.Machine,
                })
                .ToListAsync(cancellationToken)
        )
            revisions[row.RowId] = new PolledRow(row.Revision, row.Machine);
        return revisions;
    }

    // One row as a poll read it: its revision, and its machine, so a poll limited to some machines tells a row that is
    // gone from one it no longer reads.
    private readonly record struct PolledRow(long Revision, string Machine);

    // What one poll reads: the rows watched one by one, and every row of Machines (null: of every machine) as well
    // when EveryRow is set.
    private sealed record PollScope(HashSet<long> Rows, bool EveryRow, HashSet<string>? Machines)
    {
        // Whether this poll read every row the next one reads in full, so the next can compare with it.
        public bool Covers(PollScope next) =>
            EveryRow
            && (
                Machines is null
                || (next.Machines is not null && next.Machines.IsSubsetOf(Machines))
            );

        // Whether this poll reads every row of the machine.
        public bool Reads(string machine) =>
            EveryRow && (Machines is null || Machines.Contains(machine));
    }

    // A notice's payload is the row_id as decimal digits; anything else (a sign, a space, a number too large, an
    // empty string) is not one of the trigger's notices.
    internal static bool TryParseRowId(string payload, out long rowId) =>
        long.TryParse(payload, NumberStyles.None, CultureInfo.InvariantCulture, out rowId)
        && rowId > 0;

    // A loop that was stopped (its last reader left) and replaced must not speak for the one that replaced it.
    private void Live(CancellationToken loop = default)
    {
        lock (_gate)
        {
            if (loop.IsCancellationRequested)
                return;
            _listening = true;
            foreach (var reader in _readers)
                reader.Write(SnapshotChange.Reload.Instance);
        }
    }

    private void Lost(CancellationToken loop = default)
    {
        lock (_gate)
            if (!loop.IsCancellationRequested)
                _listening = false;
        StateMachineMeter.ForgetQueueUsage(this);
    }

    private void Broadcast(SnapshotChange change, CancellationToken loop = default)
    {
        lock (_gate)
        {
            if (loop.IsCancellationRequested)
                return;
            foreach (var reader in _readers)
                reader.Write(change);
        }
    }

    private static async Task<bool> Wait(TimeSpan delay, CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(delay, stoppingToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    // One reader's buffer. A change that does not fit is dropped and the reader is owed a reload, which it reads
    // before anything still buffered.
    private sealed class Reader(Func<SnapshotWatchScope?>? watched)
    {
        /// <summary>The rows this reader watches (null: every row), asked before each poll.</summary>
        public Func<SnapshotWatchScope?>? Watched { get; } = watched;

        private readonly Channel<SnapshotChange> _buffer = Channel.CreateBounded<SnapshotChange>(
            new BoundedChannelOptions(ReaderCapacity)
            {
                SingleReader = true,
                FullMode = BoundedChannelFullMode.Wait,
            }
        );
        private int _owedReload;

        public void Write(SnapshotChange change)
        {
            if (_buffer.Writer.TryWrite(change))
                return;
            Interlocked.Exchange(ref _owedReload, 1);
        }

        // The next change, or null once the host stops. A cancellation of the reader's own token throws.
        public async ValueTask<SnapshotChange?> TryReadAsync(
            CancellationToken cancellationToken,
            CancellationToken stopping
        )
        {
            if (Interlocked.Exchange(ref _owedReload, 0) == 1)
                return SnapshotChange.Reload.Instance;
            try
            {
                return await _buffer.Reader.ReadAsync(cancellationToken);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                return null;
            }
        }
    }
}
