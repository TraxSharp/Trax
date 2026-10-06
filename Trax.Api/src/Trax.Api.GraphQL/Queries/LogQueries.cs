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
    /// A page of log records written by trains, newest first unless <c>order</c> says otherwise.
    /// Pass the previous page's <c>nextCursor</c> as <c>afterId</c> to page deeply in either order;
    /// <c>skip</c> is ignored when <c>afterId</c> is set. Unfiltered, the total may be an estimate
    /// (<c>isEstimatedCount</c>). Filtered by text, it counts at most 10,000 matches: past that it
    /// reads 10,000 with <c>isCountCapped</c> true, a lower bound. Under any other filter it is
    /// exact.
    /// </summary>
    /// <remarks>
    /// <c>messageContains</c> and <c>categoryContains</c> match text anywhere in the field,
    /// ignoring case, with <c>%</c>, <c>_</c> and <c>\</c> matching themselves.
    /// </remarks>
    /// <param name="operationsService">Resolved from DI; not a GraphQL argument.</param>
    /// <param name="dataContextFactory">Resolved from DI; not a GraphQL argument.</param>
    /// <param name="ct">Cancels the read.</param>
    /// <param name="skip">How many records to skip (negative is treated as 0; above 10,000 is refused with <c>TRAX_SKIP_TOO_DEEP</c>, so page deeper with <c>afterId</c>).</param>
    /// <param name="take">The page size, clamped to 1 through 500.</param>
    /// <param name="metadataId">Only records written by this execution.</param>
    /// <param name="minimumLevel">Only records at this level or above.</param>
    /// <param name="category">Only records with exactly this logger category.</param>
    /// <param name="afterId">A keyset cursor: the page continues after this id in the chosen order.</param>
    /// <param name="messageContains">Only records whose message contains this text, ignoring case.</param>
    /// <param name="categoryContains">Only records whose logger category contains this text, ignoring case.</param>
    /// <param name="order">Newest first (the default) or oldest first, as a run's log reads.</param>
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
        string? messageContains = null,
        string? categoryContains = null,
        SortOrder order = SortOrder.Newest,
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
        )
        {
            MessageContains = messageContains,
            CategoryContains = categoryContains,
            Order = order == SortOrder.Oldest ? LogOrder.OldestFirst : LogOrder.NewestFirst,
        };

        var page = await operationsService.GetLogsAsync(query, ct);

        var hasFilter =
            metadataId.HasValue
            || minimumLevel.HasValue
            || !string.IsNullOrWhiteSpace(category)
            || !string.IsNullOrEmpty(messageContains)
            || !string.IsNullOrEmpty(categoryContains);

        // A text filter counts up to OperationsService.LogCountCap matches and no further, so a
        // common term never counts the whole table; the service counts every other filter
        // exactly. An unfiltered count of the log table is a full scan, so that one total is the
        // database's estimate when there is a usable one.
        int totalCount;
        bool isEstimate = false;
        bool isCapped = false;
        if (hasFilter)
        {
            var count = await operationsService.CountLogsCappedAsync(query, ct);
            (totalCount, isCapped) = (count.Count, count.Capped);
        }
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
        )
        {
            IsCountCapped = isCapped,
        };
    }
}
