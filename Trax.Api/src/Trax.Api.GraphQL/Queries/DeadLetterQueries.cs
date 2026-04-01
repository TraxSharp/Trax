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
    public async Task<PagedResult<DeadLetterSummary>> GetDeadLetters(
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct,
        int skip = 0,
        int take = 25,
        DeadLetterStatus? status = null,
        long? afterId = null
    )
    {
        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        var baseQuery = db.DeadLetters.AsNoTracking().OrderByDescending(dl => dl.Id);

        IQueryable<Effect.Models.DeadLetter.DeadLetter> query = baseQuery;

        if (status.HasValue)
            query = query.Where(dl => dl.Status == status.Value);

        if (afterId.HasValue)
            query = query.Where(dl => dl.Id < afterId.Value);

        var totalCount = await query.CountAsync(ct);

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
            skip,
            take,
            NextCursor: nextCursor
        );
    }

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
