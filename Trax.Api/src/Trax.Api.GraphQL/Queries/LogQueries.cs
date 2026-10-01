using Microsoft.Extensions.Logging;
using Trax.Api.DTOs;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Scheduler.Services.Operations;

namespace Trax.Api.GraphQL.Queries;

/// <summary>
/// Queries over the log records trains write, for the dashboard's Logs page and ad-hoc API consumers.
/// Reads only; logs are written by the framework. The page and the filtered count come from
/// <see cref="IOperationsService"/>, which the dashboard reads too, so both apply one filter.
/// </summary>
public class LogQueries
{
    /// <summary>
    /// A page of log records written by trains, newest first. Pass the previous page's
    /// <c>nextCursor</c> as <c>afterId</c> to page deeply; <c>skip</c> is ignored when <c>afterId</c>
    /// is set. The total is exact whenever a filter is given; unfiltered, it may be an estimate.
    /// </summary>
    /// <param name="operationsService">Resolved from DI; not a GraphQL argument.</param>
    /// <param name="dataContextFactory">Resolved from DI; not a GraphQL argument.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <param name="skip">How many records to skip (negative is treated as 0).</param>
    /// <param name="take">The page size, clamped to 1 through 500.</param>
    /// <param name="metadataId">Only records written by this execution.</param>
    /// <param name="minimumLevel">Only records at this level or above.</param>
    /// <param name="category">Only records with exactly this logger category.</param>
    /// <param name="afterId">Only records older than this id (a keyset cursor).</param>
    /// <param name="sqlDialect">Resolved from DI when the provider registers one; not a GraphQL argument.</param>
    public async Task<PagedResult<LogEntry>> GetLogs(
        [Service] IOperationsService operationsService,
        [Service] IDataContextProviderFactory dataContextFactory,
        CancellationToken ct,
        int skip = 0,
        int take = 25,
        long? metadataId = null,
        LogLevel? minimumLevel = null,
        string? category = null,
        long? afterId = null,
        [Service] ISqlDialect? sqlDialect = null
    )
    {
        var query = new LogQuery(
            metadataId,
            minimumLevel,
            category,
            afterId,
            OperationsPageBounds.Skip(skip),
            OperationsPageBounds.Take(take)
        );

        var page = await operationsService.GetLogsAsync(query, ct);

        var hasFilter =
            metadataId.HasValue || minimumLevel.HasValue || !string.IsNullOrWhiteSpace(category);

        // The service counts exactly. An unfiltered count of the log table is a full scan, so
        // that one total is the database's estimate when there is a usable one.
        int totalCount;
        bool isEstimate;
        if (hasFilter)
            (totalCount, isEstimate) = (await operationsService.CountLogsAsync(query, ct), false);
        else
        {
            using var db = await dataContextFactory.CreateDbContextAsync(ct);
            (totalCount, isEstimate) = await CountEstimator.EstimateOrCountAsync(
                db,
                sqlDialect,
                "log",
                () => operationsService.CountLogsAsync(query, ct),
                ct
            );
        }

        return new PagedResult<LogEntry>(
            page.Items.Select(l => new LogEntry(
                    l.Id,
                    l.MetadataId,
                    l.EventId,
                    l.Level,
                    l.Category,
                    l.Message,
                    l.Exception,
                    l.StackTrace
                ))
                .ToList(),
            totalCount,
            page.Skip,
            page.Take,
            isEstimate,
            page.NextCursor
        );
    }
}
