using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Api.DTOs;
using Trax.Effect.Data.Services.IDataContextFactory;

namespace Trax.Api.GraphQL.Queries;

/// <summary>
/// Queries over the log records trains write, for the dashboard's Logs page and ad-hoc API consumers.
/// Reads only; logs are written by the framework.
/// </summary>
public class LogQueries
{
    /// <summary>
    /// A page of log records written by trains, newest first. Pass the previous page's
    /// <c>nextCursor</c> as <c>afterId</c> to page deeply; <c>skip</c> is ignored when <c>afterId</c>
    /// is set. The total is exact whenever a filter or cursor is given.
    /// </summary>
    /// <param name="dataContextFactory">Resolved from DI; not a GraphQL argument.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <param name="skip">How many records to skip (negative is treated as 0).</param>
    /// <param name="take">The page size, clamped to 1 through 500.</param>
    /// <param name="metadataId">Only records written by this execution.</param>
    /// <param name="minimumLevel">Only records at this level or above.</param>
    /// <param name="category">Only records with exactly this logger category.</param>
    /// <param name="afterId">Only records older than this id (a keyset cursor).</param>
    public async Task<PagedResult<LogEntry>> GetLogs(
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct,
        int skip = 0,
        int take = 25,
        long? metadataId = null,
        LogLevel? minimumLevel = null,
        string? category = null,
        long? afterId = null
    )
    {
        take = OperationsPageBounds.Take(take);
        skip = OperationsPageBounds.Skip(skip);

        using var db = await dataContextFactory.CreateDbContextAsync(ct);

        IQueryable<Effect.Models.Log.Log> baseQuery = db
            .Logs.AsNoTracking()
            .OrderByDescending(l => l.Id);

        if (metadataId.HasValue)
            baseQuery = baseQuery.Where(l => l.MetadataId == metadataId.Value);

        if (minimumLevel.HasValue)
            baseQuery = baseQuery.Where(l => l.Level >= minimumLevel.Value);

        if (!string.IsNullOrWhiteSpace(category))
            baseQuery = baseQuery.Where(l => l.Category == category);

        var hasFilter =
            metadataId.HasValue || minimumLevel.HasValue || !string.IsNullOrWhiteSpace(category);

        var (totalCount, isEstimate) =
            (afterId.HasValue || hasFilter)
                ? (await baseQuery.CountAsync(ct), false)
                : await CountEstimator.EstimateOrCountAsync(
                    db,
                    "log",
                    () => baseQuery.CountAsync(ct),
                    ct
                );

        var query = afterId.HasValue ? baseQuery.Where(l => l.Id < afterId.Value) : baseQuery;

        if (!afterId.HasValue && skip > 0)
            query = query.Skip(skip);

        var items = await query
            .Take(take)
            .Select(l => new LogEntry(
                l.Id,
                l.MetadataId,
                l.EventId,
                l.Level,
                l.Category,
                l.Message,
                l.Exception,
                l.StackTrace
            ))
            .ToListAsync(ct);

        var nextCursor = items.Count > 0 ? items[^1].Id : (long?)null;

        return new PagedResult<LogEntry>(
            items,
            totalCount,
            afterId.HasValue ? 0 : skip,
            take,
            isEstimate,
            nextCursor
        );
    }
}
