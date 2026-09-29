using Microsoft.EntityFrameworkCore;
using Trax.Api.DTOs;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;

namespace Trax.Api.GraphQL.Queries;

/// <summary>
/// Queries for the work queue: pending, dispatched, and cancelled entries with optional
/// status / train name filtering and keyset pagination.
/// </summary>
public class WorkQueueQueries
{
    /// <summary>
    /// A page of work queue entries, newest first. Pass the previous page's <c>nextCursor</c> as
    /// <c>afterId</c> to page deeply; <c>skip</c> is ignored when <c>afterId</c> is set. Carries no
    /// train input; <c>detail</c> does.
    /// </summary>
    /// <param name="dataContextFactory">Resolved from DI; not a GraphQL argument.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <param name="skip">How many entries to skip (negative is treated as 0).</param>
    /// <param name="take">The page size, clamped to 1 through 500.</param>
    /// <param name="status">Only entries in this status.</param>
    /// <param name="trainName">Only entries for this train (matched exactly).</param>
    /// <param name="afterId">Only entries older than this id (a keyset cursor).</param>
    public async Task<PagedResult<WorkQueueSummary>> GetWorkQueues(
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct,
        int skip = 0,
        int take = 25,
        WorkQueueStatus? status = null,
        string? trainName = null,
        long? afterId = null
    )
    {
        take = OperationsPageBounds.Take(take);
        skip = OperationsPageBounds.Skip(skip);

        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        IQueryable<Effect.Models.WorkQueue.WorkQueue> baseQuery = db
            .WorkQueues.AsNoTracking()
            .OrderByDescending(q => q.Id);

        if (status.HasValue)
            baseQuery = baseQuery.Where(q => q.Status == status.Value);

        if (!string.IsNullOrWhiteSpace(trainName))
            baseQuery = baseQuery.Where(q => q.TrainName == trainName);

        var hasFilter = status.HasValue || !string.IsNullOrWhiteSpace(trainName);

        // CountEstimator only applies when there are no filters AND no cursor —
        // otherwise we must use an exact count.
        var (totalCount, isEstimate) =
            (afterId.HasValue || hasFilter)
                ? (await baseQuery.CountAsync(ct), false)
                : await CountEstimator.EstimateOrCountAsync(
                    db,
                    "work_queue",
                    () => baseQuery.CountAsync(ct),
                    ct
                );

        var query = afterId.HasValue ? baseQuery.Where(q => q.Id < afterId.Value) : baseQuery;

        if (!afterId.HasValue && skip > 0)
            query = query.Skip(skip);

        var items = await query
            .Take(take)
            .Select(q => new WorkQueueSummary(
                q.Id,
                q.ExternalId,
                q.TrainName,
                q.Status,
                q.CreatedAt,
                q.DispatchedAt,
                q.ScheduledAt,
                q.Priority,
                q.DispatchAttempts,
                q.ManifestId,
                q.MetadataId,
                q.DeadLetterId,
                q.InputTypeName,
                q.ConfirmedAt,
                q.SubjectKey
            ))
            .ToListAsync(ct);

        var nextCursor = items.Count > 0 ? items[^1].Id : (long?)null;

        return new PagedResult<WorkQueueSummary>(
            items,
            totalCount,
            afterId.HasValue ? 0 : skip,
            take,
            isEstimate,
            nextCursor
        );
    }

    /// <summary>
    /// One work queue entry by id, without its train input, or <c>null</c> when none has that id.
    /// </summary>
    public async Task<WorkQueueSummary?> GetWorkQueue(
        long id,
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct
    )
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        return await db
            .WorkQueues.AsNoTracking()
            .Where(q => q.Id == id)
            .Select(q => new WorkQueueSummary(
                q.Id,
                q.ExternalId,
                q.TrainName,
                q.Status,
                q.CreatedAt,
                q.DispatchedAt,
                q.ScheduledAt,
                q.Priority,
                q.DispatchAttempts,
                q.ManifestId,
                q.MetadataId,
                q.DeadLetterId,
                q.InputTypeName,
                q.ConfirmedAt,
                q.SubjectKey
            ))
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Full detail for one work queue entry: its train input, and for a queued entry with a
    /// subject, what it is waiting on. The input is on this single-row read only, never on the
    /// <c>workQueues</c> list, the way an execution's input is on <c>executionDetail</c> alone.
    /// Returns <c>null</c> when the entry does not exist.
    /// </summary>
    public async Task<WorkQueueDetail?> GetDetail(
        long id,
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct
    )
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        var entry = await db.WorkQueues.AsNoTracking().FirstOrDefaultAsync(q => q.Id == id, ct);
        if (entry is null)
            return null;

        long? heldBy = null;
        long? queuedBehind = null;
        if (entry is { Status: WorkQueueStatus.Queued, SubjectKey: { } subject })
        {
            // A queued entry whose subject has a run in flight is skipped by dispatch until
            // that run finishes.
            heldBy = await db
                .WorkQueues.AsNoTracking()
                .Where(b =>
                    b.SubjectKey == subject
                    && b.Status == WorkQueueStatus.Dispatched
                    && b.Metadata != null
                    && (
                        b.Metadata.TrainState == TrainState.Pending
                        || b.Metadata.TrainState == TrainState.InProgress
                    )
                )
                .Select(b => (long?)b.Id)
                .FirstOrDefaultAsync(ct);

            // Dispatch also offers only the first queued entry per subject each cycle, so an
            // entry behind a sibling it would take first waits even with nothing running.
            if (heldBy is null)
                queuedBehind = await DispatchedAheadOf(
                        db.WorkQueues.AsNoTracking(),
                        entry,
                        DateTime.UtcNow
                    )
                    .Select(b => (long?)b.Id)
                    .FirstOrDefaultAsync(ct);
        }

        return new WorkQueueDetail(
            entry.Id,
            entry.ExternalId,
            entry.TrainName,
            entry.Status,
            entry.CreatedAt,
            entry.DispatchedAt,
            entry.ScheduledAt,
            entry.Priority,
            entry.DispatchAttempts,
            entry.ManifestId,
            entry.MetadataId,
            entry.DeadLetterId,
            entry.InputTypeName,
            entry.ConfirmedAt,
            entry.SubjectKey,
            entry.Input,
            heldBy,
            queuedBehind
        );
    }

    /// <summary>
    /// The confirmed, queued entries for <paramref name="entry"/>'s subject that dispatch would
    /// take before it, most-favoured first.
    /// </summary>
    /// <remarks>
    /// This has to agree with the scheduler's <c>LoadQueuedJobsJunction</c>, and it is the same
    /// predicate the Blazor dashboard's work queue detail page uses: an entry not yet due is not a
    /// candidate, nor is one whose manifest group is disabled, and among the rest priority then
    /// age decides. Group priority is not compared because only the mediator's queue path sets a
    /// subject key and those entries carry no manifest.
    /// </remarks>
    private static IQueryable<Effect.Models.WorkQueue.WorkQueue> DispatchedAheadOf(
        IQueryable<Effect.Models.WorkQueue.WorkQueue> source,
        Effect.Models.WorkQueue.WorkQueue entry,
        DateTime now
    ) =>
        source
            .Where(b =>
                b.SubjectKey == entry.SubjectKey
                && b.Id != entry.Id
                && b.Status == WorkQueueStatus.Queued
                && b.ConfirmedAt != null
                && (b.ScheduledAt == null || b.ScheduledAt <= now)
                && (b.ManifestId == null || b.Manifest!.ManifestGroup!.IsEnabled)
                && (
                    b.Priority > entry.Priority
                    || (b.Priority == entry.Priority && b.CreatedAt < entry.CreatedAt)
                )
            )
            .OrderByDescending(b => b.Priority)
            .ThenBy(b => b.CreatedAt);
}
