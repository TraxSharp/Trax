using Microsoft.EntityFrameworkCore;
using Trax.Api.DTOs;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Enums;
using Trax.Scheduler.Services.Operations;

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
    /// <param name="sqlDialect">Resolved from DI when the provider registers one; not a GraphQL argument.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <param name="skip">How many entries to skip (negative is treated as 0; above 10,000 is refused with <c>TRAX_SKIP_TOO_DEEP</c>, so page deeper with <c>afterId</c>).</param>
    /// <param name="take">The page size, clamped to 1 through 500.</param>
    /// <param name="status">Only entries in this status.</param>
    /// <param name="trainName">Only entries for this train (matched exactly).</param>
    /// <param name="afterId">Only entries older than this id (a keyset cursor).</param>
    /// <param name="subjectKey">Only entries that serialize on this subject (matched exactly).</param>
    /// <param name="manifestId">Only entries queued for this manifest.</param>
    public async Task<PagedResult<WorkQueueSummary>> GetWorkQueues(
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct,
        int skip = 0,
        int take = 25,
        WorkQueueStatus? status = null,
        string? trainName = null,
        long? afterId = null,
        string? subjectKey = null,
        long? manifestId = null,
        [Service] ISqlDialect? sqlDialect = null
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

        if (!string.IsNullOrEmpty(subjectKey))
            baseQuery = baseQuery.Where(q => q.SubjectKey == subjectKey);

        if (manifestId.HasValue)
            baseQuery = baseQuery.Where(q => q.ManifestId == manifestId.Value);

        var hasFilter =
            status.HasValue
            || !string.IsNullOrWhiteSpace(trainName)
            || !string.IsNullOrEmpty(subjectKey)
            || manifestId.HasValue;

        // An unfiltered total is estimated on every page, so the cursor never changes it.
        var (totalCount, isEstimate) = hasFilter
            ? (await baseQuery.CountAsync(ct), false)
            : await CountEstimator.EstimateOrCountAsync(
                db,
                sqlDialect,
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
            )
            {
                ReplayDecisionsOf = q.ReplayDecisionsOf,
            })
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
            )
            {
                ReplayDecisionsOf = q.ReplayDecisionsOf,
            })
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>
    /// Full detail for one work queue entry: its train input, and for a queued entry with a
    /// subject, what it is waiting on. The input is on this single-row read only, never on the
    /// <c>workQueues</c> list, the way an execution's input is on <c>executionDetail</c> alone.
    /// The entry keeps the input unmasked because the run starts from it; here each
    /// <c>[TraxSensitive]</c> member reads <c>{"_redacted": true}</c>, as in an execution's
    /// recorded input, and an input this host cannot read as its type is masked whole.
    /// Returns <c>null</c> when the entry does not exist.
    /// </summary>
    /// <remarks>
    /// Read through <see cref="IOperationsService.GetWorkQueueEntryDetailAsync"/>, the call the
    /// dashboard's work queue entry page makes.
    /// </remarks>
    public async Task<WorkQueueDetail?> GetDetail(
        long id,
        [Service] IOperationsService operationsService,
        CancellationToken ct
    ) =>
        await operationsService.GetWorkQueueEntryDetailAsync(id, ct) is { } entry
            ? new WorkQueueDetail(
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
                entry.SubjectHeldBy,
                entry.SubjectQueuedBehind
            )
            {
                ReplayDecisionsOf = entry.ReplayDecisionsOf,
            }
            : null;
}
