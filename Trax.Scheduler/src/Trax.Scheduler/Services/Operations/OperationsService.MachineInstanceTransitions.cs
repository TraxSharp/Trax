using Microsoft.EntityFrameworkCore;
using Trax.Effect.Enums;

namespace Trax.Scheduler.Services.Operations;

// The operator's read of one state-machine instance's history (trax.snapshot_transition): the one path the
// dashboard's instance page and the API's machineInstance { transitions } share (central docs/0022). Like the rest of
// the instance reads, nothing here reads or returns a snapshot's context, its owner's key or a run's token.
public partial class OperationsService
{
    /// <inheritdoc />
    /// <remarks>
    /// The instance is found as <see cref="GetMachineInstanceAsync"/> finds it, and its history is read by its row id
    /// through <c>uq_snapshot_transition_draft_revision</c> (Trax.Effect's Postgres migration 089), newest revision
    /// first, so a page costs the rows it returns whatever the history's length. One row past the page tells whether
    /// there is another, and gives the last transition on the page its <see cref="MachineInstanceTransitionRecord.PreviousAt"/>.
    /// What each transition started is read by the tokens on the page: runs and queue entries by their external id,
    /// children by the token they share with the parent.
    /// </remarks>
    public async Task<MachineInstanceTransitionPage?> GetMachineInstanceTransitionsAsync(
        MachineInstanceKey key,
        long? beforeRevision,
        int take,
        CancellationToken ct
    )
    {
        ValidateLookup(key);
        take = Math.Clamp(take, 1, MaxPageSize);

        using var db = await _dataContextFactory.CreateDbContextAsync(ct);
        var rowId = await InstanceRows(db, key).Select(x => (long?)x.RowId).FirstOrDefaultAsync(ct);
        if (rowId is not { } row)
            return null;

        var history = db.SnapshotTransitions.AsNoTracking().Where(t => t.DraftRowId == row);
        if (beforeRevision is { } before)
            history = history.Where(t => t.Revision < before);
        var rows = await history
            .OrderByDescending(t => t.Revision)
            .Take(take + 1)
            .Select(t => new
            {
                t.Revision,
                t.Kind,
                t.Trigger,
                t.Actor,
                t.FromState,
                t.ToState,
                t.At,
                t.InvokeToken,
            })
            .ToListAsync(ct);

        var page = rows.Take(take).ToList();
        var tokens = page.Where(t => t.InvokeToken != null)
            .Select(t => t.InvokeToken!)
            .Distinct()
            .ToList();

        Dictionary<string, (long Id, TrainState State)> runs = new(StringComparer.Ordinal);
        Dictionary<string, (long Id, WorkQueueStatus Status)> entries = new(StringComparer.Ordinal);
        Dictionary<string, MachineInstanceStartedChild> children = new(StringComparer.Ordinal);
        if (tokens.Count > 0)
        {
            foreach (
                var run in await db
                    .Metadatas.AsNoTracking()
                    .Where(m => tokens.Contains(m.ExternalId))
                    .Select(m => new
                    {
                        m.ExternalId,
                        m.Id,
                        m.TrainState,
                    })
                    .ToListAsync(ct)
            )
                runs[run.ExternalId] = (run.Id, run.TrainState);
            foreach (
                var entry in await db
                    .WorkQueues.AsNoTracking()
                    .Where(w => tokens.Contains(w.ExternalId))
                    .Select(w => new
                    {
                        w.ExternalId,
                        w.Id,
                        w.Status,
                    })
                    .ToListAsync(ct)
            )
                entries[entry.ExternalId] = (entry.Id, entry.Status);
            // A child lives under its parent's owner and shares the token the parent held for it.
            foreach (
                var child in await db
                    .SnapshotDrafts.AsNoTracking()
                    .Where(d =>
                        d.SpawnedBy != null
                        && tokens.Contains(d.SpawnedBy)
                        && d.OwnerKind == key.OwnerKind
                    )
                    .Select(d => new
                    {
                        d.SpawnedBy,
                        d.Machine,
                        d.OwnerKind,
                        d.Id,
                        d.RowId,
                    })
                    .ToListAsync(ct)
            )
                children[child.SpawnedBy!] = new MachineInstanceStartedChild(
                    child.Machine,
                    child.OwnerKind,
                    child.Id,
                    child.RowId
                );
        }

        var items = page.Select(
                (t, i) =>
                {
                    MachineInstanceStartedRun? startedRun = null;
                    MachineInstanceStartedChild? startedChild = null;
                    if (t.InvokeToken is { } token)
                    {
                        if (children.TryGetValue(token, out var child))
                            startedChild = child;
                        else
                        {
                            var hasRun = runs.TryGetValue(token, out var run);
                            var hasEntry = entries.TryGetValue(token, out var entry);
                            TrainState? state =
                                hasRun ? run.State
                                : hasEntry
                                    ? entry.Status == WorkQueueStatus.Cancelled
                                            ? TrainState.Cancelled
                                        : TrainState.Pending
                                : null;
                            startedRun = new MachineInstanceStartedRun(
                                hasRun ? run.Id : null,
                                state,
                                hasEntry ? entry.Id : null
                            );
                        }
                    }
                    return new MachineInstanceTransitionRecord(
                        t.Revision,
                        t.Kind,
                        t.Trigger,
                        t.Actor,
                        t.FromState,
                        t.ToState,
                        t.At
                    )
                    {
                        PreviousAt = i + 1 < rows.Count ? rows[i + 1].At : null,
                        StartedRun = startedRun,
                        StartedChild = startedChild,
                    };
                }
            )
            .ToList();

        return new MachineInstanceTransitionPage(
            items,
            take,
            rows.Count > take ? page[^1].Revision : null
        );
    }
}
