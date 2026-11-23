using Microsoft.EntityFrameworkCore;
using Trax.Core.Extensions;
using Trax.Effect.Data.Services.DataContext;
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

        var db = await _dataContextFactory.CreateDbContextAsync(ct).ConfigureAwait(false);
        await using var dbDisposal = db.ConfigureAwait(false);
        var rowId = await InstanceRows(db, key)
            .Select(x => (long?)x.RowId)
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);
        if (rowId is not { } row)
            return null;

        var rows = await ReadTransitionRowsAsync(db, row, beforeRevision, take, ct)
            .ConfigureAwait(false);

        var page = rows.Take(take).ToList();
        var tokens = page.Where(t => t.InvokeToken != null)
            .Select(t => t.InvokeToken.NotNull())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var started = await StartedByToken
            .ReadAsync(db, tokens, key.OwnerKind, ct)
            .ConfigureAwait(false);

        var items = page.Select((t, i) => TransitionRecord(t, i, rows, started)).ToList();

        return new MachineInstanceTransitionPage(
            items,
            take,
            rows.Count > take ? page[^1].Revision : null
        );
    }

    // A row of an instance's history, as the page reads it.
    private sealed record TransitionRow(
        long Revision,
        SnapshotTransitionKind Kind,
        string? Trigger,
        SnapshotTransitionActor Actor,
        string? FromState,
        string ToState,
        DateTimeOffset At,
        string? InvokeToken
    );

    private static async Task<List<TransitionRow>> ReadTransitionRowsAsync(
        IDataContext db,
        long row,
        long? beforeRevision,
        int take,
        CancellationToken ct
    )
    {
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
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return rows.Select(t => new TransitionRow(
                t.Revision,
                t.Kind,
                t.Trigger,
                t.Actor,
                t.FromState,
                t.ToState,
                t.At,
                t.InvokeToken
            ))
            .ToList();
    }

    private static MachineInstanceTransitionRecord TransitionRecord(
        TransitionRow t,
        int i,
        List<TransitionRow> rows,
        StartedByToken started
    )
    {
        MachineInstanceStartedRun? startedRun = null;
        MachineInstanceStartedChild? startedChild = null;
        if (t.InvokeToken is { } token)
        {
            if (started.Children.TryGetValue(token, out var child))
                startedChild = child;
            else
            {
                var hasRun = started.Runs.TryGetValue(token, out var run);
                var hasEntry = started.Entries.TryGetValue(token, out var entry);
                TrainState? state =
                    hasRun ? run.State
                    : hasEntry
                        ? entry.Status == WorkQueueStatus.Cancelled ? TrainState.Cancelled
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

    // What the transitions on a page started, by the token each holds.
    private sealed class StartedByToken
    {
        public Dictionary<string, (long Id, TrainState State)> Runs { get; } =
            new(StringComparer.Ordinal);

        public Dictionary<string, (long Id, WorkQueueStatus Status)> Entries { get; } =
            new(StringComparer.Ordinal);

        public Dictionary<string, MachineInstanceStartedChild> Children { get; } =
            new(StringComparer.Ordinal);

        public static async Task<StartedByToken> ReadAsync(
            IDataContext db,
            List<string> tokens,
            SnapshotOwnerKind ownerKind,
            CancellationToken ct
        )
        {
            var started = new StartedByToken();
            if (tokens.Count > 0)
            {
                await started.ReadRunsAndEntriesAsync(db, tokens, ct).ConfigureAwait(false);
                await started.ReadChildrenAsync(db, tokens, ownerKind, ct).ConfigureAwait(false);
            }
            return started;
        }

        private async Task ReadRunsAndEntriesAsync(
            IDataContext db,
            List<string> tokens,
            CancellationToken ct
        )
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
                    .ConfigureAwait(false)
            )
                Runs[run.ExternalId] = (run.Id, run.TrainState);
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
                    .ConfigureAwait(false)
            )
                Entries[entry.ExternalId] = (entry.Id, entry.Status);
        }

        private async Task ReadChildrenAsync(
            IDataContext db,
            List<string> tokens,
            SnapshotOwnerKind ownerKind,
            CancellationToken ct
        )
        {
            // A child lives under its parent's owner and shares the token the parent held for it.
            foreach (
                var child in await db
                    .SnapshotDrafts.AsNoTracking()
                    .Where(d =>
                        d.SpawnedBy != null
                        && tokens.Contains(d.SpawnedBy)
                        && d.OwnerKind == ownerKind
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
                    .ConfigureAwait(false)
            )
                Children[child.SpawnedBy.NotNull()] = new MachineInstanceStartedChild(
                    child.Machine,
                    child.OwnerKind,
                    child.Id,
                    child.RowId
                );
        }
    }
}
