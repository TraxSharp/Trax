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
                q.InputTypeName
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
                q.InputTypeName
            ))
            .FirstOrDefaultAsync(ct);
    }
}
