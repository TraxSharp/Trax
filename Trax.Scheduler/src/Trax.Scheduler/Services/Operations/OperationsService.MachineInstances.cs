using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
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

    /// <inheritdoc />
    /// <remarks>
    /// Newest first by <c>updated_at</c>, then by row id so rows written in the same instant keep
    /// one order between pages. A page under one machine and state reads
    /// <c>ix_snapshot_draft_machine_state_updated</c> in that order; any other filter reads
    /// <c>ix_snapshot_draft_updated</c> (Trax.Effect's Postgres migration 071).
    /// </remarks>
    public async Task<MachineInstancePage> GetMachineInstancesAsync(
        MachineInstanceQuery query,
        CancellationToken ct
    )
    {
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
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(key.Machine, nameof(key));
        if (key.OwnerKind == SnapshotOwnerKind.User && key.RowId is null)
            throw new ArgumentException(
                "A user's draft is named by its row id as well as its machine and id: several "
                    + "users can each hold a draft under one id, and an operator is not shown "
                    + "whose a draft is. Pass the rowId the listing gives the instance.",
                nameof(key)
            );

        using var db = await _dataContextFactory.CreateDbContextAsync(ct);
        var rows = OfOwnerKind(db.SnapshotDrafts.AsNoTracking(), key.OwnerKind)
            .Where(x => x.Machine == key.Machine && x.Id == key.Id);
        if (key.RowId is { } rowId)
            rows = rows.Where(x => x.RowId == rowId);

        return await rows.Select(ToMachineInstanceRecord).FirstOrDefaultAsync(ct);
    }

    /// <inheritdoc />
    /// <remarks>
    /// On Postgres the counts read <c>ix_snapshot_draft_machine_state_updated</c> alone, which
    /// includes the owner kind, so they cost the index rather than the table.
    /// </remarks>
    public async Task<IReadOnlyList<MachineInstanceStateCount>> GetMachineInstanceStateCountsAsync(
        string? machine,
        CancellationToken ct
    )
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
