using Microsoft.Extensions.Logging;

namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// Which log entries to read, and which page of them. Used by
/// <see cref="IOperationsService.GetLogsAsync"/> and, for the filter alone,
/// <see cref="IOperationsService.CountLogsAsync"/>.
/// </summary>
/// <param name="MetadataId">Only the entries of this run.</param>
/// <param name="MinimumLevel">Only entries at this level or above.</param>
/// <param name="Category">Only entries whose category is exactly this.</param>
/// <param name="AfterId">
/// Keyset cursor: only entries older than this id (a page's <see cref="LogPage.NextCursor"/>).
/// When set, <paramref name="Skip"/> is ignored. Prefer it to <paramref name="Skip"/>, whose
/// cost grows with the offset.
/// </param>
/// <param name="Skip">Offset into the newest-first list, when no cursor is given.</param>
/// <param name="Take">
/// Page size, clamped to 1 through <see cref="OperationsService.MaxPageSize"/>.
/// </param>
public record LogQuery(
    long? MetadataId = null,
    LogLevel? MinimumLevel = null,
    string? Category = null,
    long? AfterId = null,
    int Skip = 0,
    int Take = 25
);

/// <summary>One log entry, as <see cref="IOperationsService.GetLogsAsync"/> returns it.</summary>
public record LogRecord(
    long Id,
    long MetadataId,
    int EventId,
    LogLevel Level,
    string Category,
    string Message,
    string? Exception,
    string? StackTrace
);

/// <summary>A page of log entries, newest first.</summary>
/// <param name="Items">The entries on this page.</param>
/// <param name="Skip">The offset used: 0 when the page was read by cursor.</param>
/// <param name="Take">The page size used, after clamping.</param>
/// <param name="NextCursor">
/// The id of the last entry, to pass as <see cref="LogQuery.AfterId"/> for the next page; null
/// when the page is empty.
/// </param>
public record LogPage(IReadOnlyList<LogRecord> Items, int Skip, int Take, long? NextCursor);
