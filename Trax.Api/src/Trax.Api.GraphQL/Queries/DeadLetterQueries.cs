using Microsoft.EntityFrameworkCore;
using Trax.Api.DTOs;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;

namespace Trax.Api.GraphQL.Queries;

/// <summary>
/// Queries for dead letter records with optional status filtering and pagination.
/// </summary>
public class DeadLetterQueries
{
    /// <summary>
    /// A page of dead letters, newest first. Pass the previous page's <c>nextCursor</c> as
    /// <c>afterId</c> to page deeply; <c>skip</c> is ignored when <c>afterId</c> is set.
    /// </summary>
    /// <param name="dataContextFactory">Resolved from DI; not a GraphQL argument.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <param name="skip">How many dead letters to skip (negative is treated as 0; above 10,000 is refused with <c>TRAX_SKIP_TOO_DEEP</c>, so page deeper with <c>afterId</c>).</param>
    /// <param name="take">The page size, clamped to 1 through 500.</param>
    /// <param name="status">Only dead letters in this status; <c>null</c> for all.</param>
    /// <param name="afterId">Only dead letters older than this id (a keyset cursor).</param>
    /// <param name="manifestId">Only dead letters of this manifest.</param>
    public async Task<PagedResult<DeadLetterSummary>> GetDeadLetters(
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct,
        int skip = 0,
        int take = 25,
        DeadLetterStatus? status = null,
        long? afterId = null,
        long? manifestId = null
    )
    {
        take = OperationsPageBounds.Take(take);
        skip = OperationsPageBounds.Skip(skip);

        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        var baseQuery = db.DeadLetters.AsNoTracking().OrderByDescending(dl => dl.Id);

        IQueryable<Effect.Models.DeadLetter.DeadLetter> query = baseQuery;

        if (status.HasValue)
            query = query.Where(dl => dl.Status == status.Value);

        if (manifestId.HasValue)
            query = query.Where(dl => dl.ManifestId == manifestId.Value);

        // The total is every record the filter matches, whatever page this is, so it is counted
        // before the cursor narrows the query.
        var totalCount = await query.CountAsync(ct);

        if (afterId.HasValue)
            query = query.Where(dl => dl.Id < afterId.Value);

        if (!afterId.HasValue && skip > 0)
            query = query.Skip(skip);

        var items = await query
            .Take(take)
            .Include(dl => dl.Manifest)
            .Select(dl => new DeadLetterSummary(
                dl.Id,
                dl.ManifestId,
                dl.Manifest != null ? dl.Manifest.Name : "Unknown",
                dl.Status,
                dl.DeadLetteredAt,
                dl.Reason,
                dl.RetryCountAtDeadLetter,
                dl.ResolvedAt,
                dl.ResolutionNote,
                dl.RetryMetadataId
            ))
            .ToListAsync(ct);

        var nextCursor = items.Count > 0 ? items[^1].Id : (long?)null;

        return new PagedResult<DeadLetterSummary>(
            items,
            totalCount,
            afterId.HasValue ? 0 : skip,
            take,
            NextCursor: nextCursor
        );
    }

    /// <summary>
    /// A requeue-all job <c>requeueAllDeadLetters</c> started on this node, or <c>null</c> when this
    /// node does not know the id: it was started on another node, this node has restarted since,
    /// or it finished more than 24 hours ago. A job is read on the node that started it; the
    /// backlog itself is <c>deadLetters(status: AWAITING_INTERVENTION).totalCount</c> on any node.
    /// </summary>
    public DeadLetterRequeueJob? GetRequeueAllJob(
        Guid id,
        [Service] Trax.Scheduler.Services.DeadLetterRequeue.IDeadLetterRequeueJobs jobs
    ) => jobs.Get(id) is { } job ? DeadLetterRequeueJob.From(job) : null;

    /// <summary>
    /// One dead letter by id, or <c>null</c> when none has that id.
    /// </summary>
    public async Task<DeadLetterSummary?> GetDeadLetter(
        long id,
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct
    )
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        return await db
            .DeadLetters.AsNoTracking()
            .Include(dl => dl.Manifest)
            .Where(dl => dl.Id == id)
            .Select(dl => new DeadLetterSummary(
                dl.Id,
                dl.ManifestId,
                dl.Manifest != null ? dl.Manifest.Name : "Unknown",
                dl.Status,
                dl.DeadLetteredAt,
                dl.Reason,
                dl.RetryCountAtDeadLetter,
                dl.ResolvedAt,
                dl.ResolutionNote,
                dl.RetryMetadataId
            ))
            .FirstOrDefaultAsync(ct);
    }
}
