using System.Linq.Expressions;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Services.ChangeSignal;
using Trax.Effect.StateMachine.Persistence;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.CancellationRegistry;
using SnapshotDraft = Trax.Effect.Models.SnapshotDraft.SnapshotDraft;

namespace Trax.Scheduler.Services.Operations;

// The operator's read-only view of state-machine instances (trax.snapshot_draft): the one path
// the dashboard's State machines page and the API's operations.machineInstances share (central
// docs/0022). Nothing here reads or returns a snapshot's context or its owner's key.
public partial class OperationsService
{
    /// <summary>
    /// The most instances <see cref="CountMachineInstancesAsync"/> counts. A count past it reads
    /// as this many, with <see cref="MachineInstanceTotal.Capped"/> set. It matches the deepest
    /// offset a pager can reach, so a capped count never hides a page anyone could open.
    /// </summary>
    public const int MachineInstanceCountCap = 10_000;

    /// <summary>
    /// How long <see cref="GetMachineInstanceStateCountsAsync"/> keeps the counts it read, per
    /// host and per machine filter, so every dashboard open on the State machines page and every
    /// API caller polling <c>machineInstanceCounts</c> share one read of the table.
    /// </summary>
    public static readonly TimeSpan MachineInstanceCountCacheDuration = TimeSpan.FromSeconds(5);

    /// <inheritdoc />
    /// <remarks>
    /// Newest first by <c>updated_at</c>, then by row id so rows written in the same instant keep
    /// one order between pages. A page under one machine and state reads
    /// <c>ix_snapshot_draft_machine_state_updated</c> in that order; any other filter reads
    /// <c>ix_snapshot_draft_updated</c> (Trax.Effect's Postgres migration 071). A negative skip
    /// reads from the start, and one past <see cref="MachineInstanceCountCap"/>, the deepest page
    /// a capped count lets a pager reach, is refused, as the API's <c>machineInstances</c> refuses
    /// it, so no caller makes the database walk further.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <see cref="MachineInstanceQuery.Skip"/> is greater than <see cref="MachineInstanceCountCap"/>.
    /// </exception>
    public async Task<MachineInstancePage> GetMachineInstancesAsync(
        MachineInstanceQuery query,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(
            query.Skip,
            MachineInstanceCountCap,
            nameof(query)
        );

        var take = Math.Clamp(query.Take, 1, MaxPageSize);
        var skip = Math.Max(query.Skip, 0);

        using var db = await _dataContextFactory.CreateDbContextAsync(ct);
        var items = await MachineInstancePageQuery(db, query, skip, take).ToListAsync(ct);
        return new MachineInstancePage(items, skip, take);
    }

    /// <inheritdoc />
    public async Task<MachineInstanceTotal> CountMachineInstancesAsync(
        MachineInstanceQuery query,
        CancellationToken ct
    )
    {
        using var db = await _dataContextFactory.CreateDbContextAsync(ct);

        // One match past the cap tells a capped count from an exact one, and the database stops
        // reading once it has found that many.
        var count = await FilterMachineInstances(db.SnapshotDrafts.AsNoTracking(), query)
            .Take(MachineInstanceCountCap + 1)
            .CountAsync(ct);
        return count > MachineInstanceCountCap
            ? new MachineInstanceTotal(MachineInstanceCountCap, Capped: true)
            : new MachineInstanceTotal(count, Capped: false);
    }

    /// <inheritdoc />
    public async Task<MachineInstanceRecord?> GetMachineInstanceAsync(
        MachineInstanceKey key,
        CancellationToken ct
    )
    {
        ValidateLookup(key);

        using var db = await _dataContextFactory.CreateDbContextAsync(ct);
        return await InstanceRows(db, key).Select(ToMachineInstanceRecord).FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// The most runs <see cref="GetMachineInstanceRunsAsync"/> lists for one instance. An
    /// instance that invoked more says so with <see cref="MachineInstanceRuns.Capped"/>; the
    /// executions list, filtered by train, reaches the rest.
    /// </summary>
    public const int MachineInstanceRunCap = 50;

    /// <inheritdoc />
    /// <remarks>
    /// A system instance's runs are read through <c>ix_metadata_invoking_instance</c> (Trax.Effect's
    /// Postgres migration 072); a user's draft's one live run through the run's unique external id.
    /// </remarks>
    public async Task<MachineInstanceRuns?> GetMachineInstanceRunsAsync(
        MachineInstanceKey key,
        CancellationToken ct
    )
    {
        ValidateLookup(key);

        using var db = await _dataContextFactory.CreateDbContextAsync(ct);
        var instance = await InstanceRows(db, key)
            .Select(x => new { x.InvokeToken })
            .FirstOrDefaultAsync(ct);
        if (instance is null)
            return null;

        var token = instance.InvokeToken;
        var linked = db
            .Metadatas.AsNoTracking()
            .Where(m =>
                m.InvokingMachine == key.Machine
                && m.InvokingInstanceId == key.Id
                && m.InvokingOwnerKind == key.OwnerKind
            );

        // A run does not record which user's draft queued it, and several users can each hold a
        // draft under one id, so a user's draft lists only the run its own token names. Listing
        // every run linked to the id could show an operator another user's runs under it.
        if (key.OwnerKind == SnapshotOwnerKind.User)
        {
            if (token is null)
                return new MachineInstanceRuns([], Capped: false, QueuedEntryId: null);
            linked = linked.Where(m => m.ExternalId == token);
        }

        var runs = await linked
            .OrderByDescending(m => m.Id)
            .Take(MachineInstanceRunCap + 1)
            .Select(m => new MachineInstanceRun(
                m.Id,
                m.ExternalId,
                m.Name,
                m.TrainState,
                m.StartTime,
                m.EndTime,
                m.FailureClass,
                m.CancellationRequested,
                token != null && m.ExternalId == token
            ))
            .ToListAsync(ct);

        long? queuedEntryId = null;
        if (token is not null && !runs.Any(r => r.IsLive))
            queuedEntryId = await db
                .WorkQueues.AsNoTracking()
                .Where(w => w.ExternalId == token && w.Status == WorkQueueStatus.Queued)
                .Select(w => (long?)w.Id)
                .FirstOrDefaultAsync(ct);

        var capped = runs.Count > MachineInstanceRunCap;
        return new MachineInstanceRuns(
            capped ? runs.Take(MachineInstanceRunCap).ToList() : runs,
            capped,
            queuedEntryId
        );
    }

    /// <inheritdoc />
    /// <remarks>
    /// The cancel is the one an operator's cancel of the run itself makes: a still-queued entry is
    /// marked Cancelled by one conditional statement, as <see cref="CancelWorkQueueEntriesAsync"/>
    /// does, and a dispatched run is flagged through the rule <see cref="CancelExecutionsAsync"/>
    /// uses. The dispatcher claims an entry and writes its run in one transaction, so the
    /// statement either finds the entry still queued, and the run never starts, or finds it
    /// claimed, and the run it then flags exists: never both, never neither. The outcome then
    /// reaches the instance through <see cref="IInvokedRunOutcomes"/>, the delivery the
    /// lifecycle hook and the reconciler make, whose conditional update on the token applies it
    /// once.
    /// </remarks>
    public async Task<MachineInstanceCancelResult> CancelMachineInstanceAsync(
        MachineInstanceKey key,
        CancellationToken ct
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(key.Machine, nameof(key));

        if (key.OwnerKind != SnapshotOwnerKind.System)
            return new MachineInstanceCancelResult(
                MachineInstanceCancelOutcome.UserOwned,
                UserOwnedCancelRefusal
            );

        string token;
        using (var db = await _dataContextFactory.CreateDbContextAsync(ct))
        {
            var instance = await InstanceRows(db, key)
                .Select(x => new { x.State, x.InvokeToken })
                .FirstOrDefaultAsync(ct);
            if (instance is null)
                return new MachineInstanceCancelResult(
                    MachineInstanceCancelOutcome.NotFound,
                    InstanceNotFoundMessage(key)
                );
            if (instance.InvokeToken is null)
                return new MachineInstanceCancelResult(
                    MachineInstanceCancelOutcome.NoLiveRun,
                    NoLiveRunMessage(key, instance.State)
                );
            token = instance.InvokeToken;

            if (BeforeMachineInstanceCancel is { } beforeCancel)
                await beforeCancel(ct);

            var queued = db.WorkQueues.Where(w =>
                w.ExternalId == token && w.Status == WorkQueueStatus.Queued
            );
            var cancelled = db.SupportsSetUpdates()
                ? await queued.ExecuteUpdateAsync(
                    s => s.SetProperty(w => w.Status, WorkQueueStatus.Cancelled),
                    ct
                )
                : await db.UpdateEachAsync(queued, w => w.Status = WorkQueueStatus.Cancelled, ct);

            if (cancelled == 0)
            {
                var flagged = await ExecutionCancellation.RequestAsync(
                    db,
                    db.Metadatas.Where(m => m.ExternalId == token),
                    _services?.GetService<ICancellationRegistry>(),
                    _changeSignal,
                    ct
                );
                return flagged > 0
                    ? new MachineInstanceCancelResult(
                        MachineInstanceCancelOutcome.CancelRequested,
                        CancelRequestedMessage(key, instance.State)
                    )
                    : new MachineInstanceCancelResult(
                        MachineInstanceCancelOutcome.RunEnded,
                        RunEndedMessage(key, instance.State)
                    );
            }

            _changeSignal?.Notify(ChangeDomain.WorkQueue);
        }

        // The run never starts. Apply its Cancelled outcome now when this host can read the
        // machine; otherwise the reconciler on a host that can does, woken on Postgres by the
        // cancelled entry's notification.
        var moved = _services?.GetService<IInvokedRunOutcomes>() is { } outcomes
            ? await outcomes.Deliver(token, ct)
            : null;
        return moved is not null
            ? new MachineInstanceCancelResult(
                MachineInstanceCancelOutcome.Moved,
                MovedMessage(key, moved),
                moved
            )
            : new MachineInstanceCancelResult(
                MachineInstanceCancelOutcome.RunCancelled,
                RunCancelledMessage(key)
            );
    }

    /// <summary>
    /// Test seam: awaited between a cancel's read of the instance's token and its conditional
    /// statement on the run's work queue entry, so a test can put the dispatcher's claim there.
    /// </summary>
    internal Func<CancellationToken, Task>? BeforeMachineInstanceCancel { get; set; }

    /// <summary>
    /// The message <see cref="CancelMachineInstanceAsync"/> refuses a user-owned instance with,
    /// on the dashboard and the API alike (<see cref="MachineInstanceCancelOutcome.UserOwned"/>).
    /// </summary>
    public const string UserOwnedCancelRefusal =
        "Operators can cancel only a system-owned instance. A user-owned instance is read-only "
        + "to operators: its run is cancelled when its user leaves the state through one of the "
        + "machine's own transitions.";

    /// <summary>The message for <see cref="MachineInstanceCancelOutcome.NotFound"/>.</summary>
    /// <param name="key">The instance asked for.</param>
    public static string InstanceNotFoundMessage(MachineInstanceKey key) =>
        $"No system-owned instance of '{key.Machine}' has id {key.Id}.";

    /// <summary>The message for <see cref="MachineInstanceCancelOutcome.NoLiveRun"/>.</summary>
    /// <param name="key">The instance asked for.</param>
    /// <param name="state">The state it is in.</param>
    public static string NoLiveRunMessage(MachineInstanceKey key, string state) =>
        $"Instance {key.Id} of '{key.Machine}' is in '{state}', which waits on no train run: "
        + "there is nothing to cancel.";

    /// <summary>The message for <see cref="MachineInstanceCancelOutcome.RunEnded"/>.</summary>
    /// <param name="key">The instance asked for.</param>
    /// <param name="state">The state it is in.</param>
    public static string RunEndedMessage(MachineInstanceKey key, string state) =>
        $"The run instance {key.Id} of '{key.Machine}' waits on in '{state}' has already ended. "
        + "Its outcome is being applied, and a cancel cannot change it.";

    /// <summary>The message for <see cref="MachineInstanceCancelOutcome.CancelRequested"/>.</summary>
    /// <param name="key">The instance asked for.</param>
    /// <param name="state">The state it is in.</param>
    public static string CancelRequestedMessage(MachineInstanceKey key, string state) =>
        $"Cancellation requested for the run instance {key.Id} of '{key.Machine}' waits on in "
        + $"'{state}'. The run stops at its next junction, and the instance then moves through "
        + "the state's OnCancelled edge.";

    /// <summary>The message for <see cref="MachineInstanceCancelOutcome.RunCancelled"/>.</summary>
    /// <param name="key">The instance asked for.</param>
    public static string RunCancelledMessage(MachineInstanceKey key) =>
        $"The queued run of instance {key.Id} of '{key.Machine}' is cancelled and will not start. "
        + "The instance moves through its state's OnCancelled edge when a host that registers "
        + "the machine applies the outcome.";

    /// <summary>The message for <see cref="MachineInstanceCancelOutcome.Moved"/>.</summary>
    /// <param name="key">The instance asked for.</param>
    /// <param name="state">The state the instance moved into.</param>
    public static string MovedMessage(MachineInstanceKey key, string state) =>
        $"The queued run of instance {key.Id} of '{key.Machine}' is cancelled and will not start, "
        + $"and the instance moved through its OnCancelled edge to '{state}'.";

    private static void ValidateLookup(MachineInstanceKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(key.Machine, nameof(key));
        if (key.OwnerKind == SnapshotOwnerKind.User && key.RowId is null)
            throw new ArgumentException(
                "A user's draft is named by its row id as well as its machine and id: several "
                    + "users can each hold a draft under one id, and an operator is not shown "
                    + "whose a draft is. Pass the rowId the listing gives the instance.",
                nameof(key)
            );
    }

    // The one row a key names: the owner kind always, and the row id when given.
    private static IQueryable<SnapshotDraft> InstanceRows(IDataContext db, MachineInstanceKey key)
    {
        var rows = OfOwnerKind(db.SnapshotDrafts.AsNoTracking(), key.OwnerKind)
            .Where(x => x.Machine == key.Machine && x.Id == key.Id);
        return key.RowId is { } rowId ? rows.Where(x => x.RowId == rowId) : rows;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>On Postgres the counts read <c>ix_snapshot_draft_machine_state_updated</c> alone,
    /// which includes the owner kind, so they cost the index rather than the table.</para>
    /// <para>The counts are kept for <see cref="MachineInstanceCountCacheDuration"/>, per host (per
    /// data context factory) and per machine filter, and callers that ask while a read is under way
    /// share it, so N dashboards polling the page make one read, not N. A count can therefore be
    /// that many seconds old.</para>
    /// </remarks>
    public async Task<IReadOnlyList<MachineInstanceStateCount>> GetMachineInstanceStateCountsAsync(
        string? machine,
        CancellationToken ct
    )
    {
        var key = string.IsNullOrWhiteSpace(machine) ? "" : machine;
        var cache = StateCountCaches.GetValue(_dataContextFactory, _ => new StateCountCache());
        var clock = _services?.GetService<TimeProvider>() ?? TimeProvider.System;

        Task<IReadOnlyList<MachineInstanceStateCount>> read;
        lock (cache)
        {
            var now = clock.GetUtcNow();
            if (
                !cache.Entries.TryGetValue(key, out var entry)
                || entry.Expires <= now
                || entry.Read.IsFaulted
                || entry.Read.IsCanceled
            )
            {
                // A caller names the machine filter, so drop what has expired rather than keep
                // one entry per name anyone ever sent.
                foreach (var stale in cache.Entries.Where(e => e.Value.Expires <= now).ToList())
                    cache.Entries.Remove(stale.Key);

                // Not tied to this caller's token: other callers wait on the same read.
                entry = new StateCountEntry(
                    ReadMachineInstanceStateCountsAsync(
                        string.IsNullOrWhiteSpace(machine) ? null : machine,
                        CancellationToken.None
                    ),
                    now + MachineInstanceCountCacheDuration
                );
                cache.Entries[key] = entry;
            }
            read = entry.Read;
        }

        return await read.WaitAsync(ct);
    }

    // One cache per data context factory, which is one per host, so two hosts in one process (a
    // test, or a host running two stores) never share counts.
    private static readonly ConditionalWeakTable<
        IDataContextProviderFactory,
        StateCountCache
    > StateCountCaches = new();

    private sealed class StateCountCache
    {
        public Dictionary<string, StateCountEntry> Entries { get; } = new(StringComparer.Ordinal);
    }

    private sealed record StateCountEntry(
        Task<IReadOnlyList<MachineInstanceStateCount>> Read,
        DateTimeOffset Expires
    );

    private async Task<
        IReadOnlyList<MachineInstanceStateCount>
    > ReadMachineInstanceStateCountsAsync(string? machine, CancellationToken ct)
    {
        using var db = await _dataContextFactory.CreateDbContextAsync(ct);
        var rows = db.SnapshotDrafts.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(machine))
            rows = rows.Where(x => x.Machine == machine);

        var counts = await rows.GroupBy(x => new
            {
                x.Machine,
                x.State,
                x.OwnerKind,
            })
            .Select(g => new
            {
                g.Key.Machine,
                g.Key.State,
                g.Key.OwnerKind,
                Count = g.LongCount(),
            })
            .ToListAsync(ct);

        return counts
            .OrderBy(c => c.Machine, StringComparer.Ordinal)
            .ThenBy(c => c.State, StringComparer.Ordinal)
            .ThenBy(c => c.OwnerKind)
            .Select(c => new MachineInstanceStateCount(c.Machine, c.State, c.OwnerKind, c.Count))
            .ToList();
    }

    /// <summary>
    /// The query <see cref="GetMachineInstancesAsync"/> runs for one page, with
    /// <paramref name="skip"/> and <paramref name="take"/> already bounded. Internal so a test
    /// can read the plan the database picks for it.
    /// </summary>
    internal static IQueryable<MachineInstanceRecord> MachineInstancePageQuery(
        IDataContext db,
        MachineInstanceQuery query,
        int skip,
        int take
    ) =>
        FilterMachineInstances(db.SnapshotDrafts.AsNoTracking(), query)
            .OrderByDescending(x => x.UpdatedAt)
            .ThenByDescending(x => x.RowId)
            .Skip(skip)
            .Take(take)
            .Select(ToMachineInstanceRecord);

    private static IQueryable<SnapshotDraft> FilterMachineInstances(
        IQueryable<SnapshotDraft> rows,
        MachineInstanceQuery query
    )
    {
        if (!string.IsNullOrWhiteSpace(query.Machine))
            rows = rows.Where(x => x.Machine == query.Machine);
        if (!string.IsNullOrWhiteSpace(query.State))
            rows = rows.Where(x => x.State == query.State);
        return query.OwnerKind is { } kind ? OfOwnerKind(rows, kind) : rows;
    }

    // The owner kind is written into the SQL as a constant, not a parameter, so the planner sees
    // which kind it is reading and can use what it knows of each kind's share of the table.
    private static IQueryable<SnapshotDraft> OfOwnerKind(
        IQueryable<SnapshotDraft> rows,
        SnapshotOwnerKind kind
    ) =>
        kind == SnapshotOwnerKind.System
            ? rows.Where(x => x.OwnerKind == SnapshotOwnerKind.System)
            : rows.Where(x => x.OwnerKind == SnapshotOwnerKind.User);

    private static readonly Expression<
        Func<SnapshotDraft, MachineInstanceRecord>
    > ToMachineInstanceRecord = x => new MachineInstanceRecord(
        x.RowId,
        x.Machine,
        x.OwnerKind,
        x.Id,
        x.State,
        x.Version,
        x.CreatedAt,
        x.UpdatedAt,
        x.InvokeToken != null
    );
}
