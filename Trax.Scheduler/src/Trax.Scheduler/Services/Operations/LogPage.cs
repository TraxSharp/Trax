using Microsoft.Extensions.Logging;

namespace Trax.Scheduler.Services.Operations;

/// <summary>
/// Which log entries to read, and which page of them. Used by
/// <see cref="IOperationsService.GetLogsAsync"/> and, for the filter alone,
/// <see cref="IOperationsService.CountLogsAsync"/> and
/// <see cref="IOperationsService.CountLogsCappedAsync"/>.
/// </summary>
/// <param name="MetadataId">Only the entries of this run.</param>
/// <param name="MinimumLevel">Only entries at this level or above.</param>
/// <param name="Category">Only entries whose category is exactly this.</param>
/// <param name="AfterId">
/// Keyset cursor: only entries after this id in the chosen <see cref="Order"/>, so older than it
/// newest first and newer than it oldest first (a page's <see cref="LogPage.NextCursor"/>). When
/// set, <paramref name="Skip"/> is ignored. Prefer it to <paramref name="Skip"/>, whose cost grows
/// with the offset.
/// </param>
/// <param name="Skip">Offset into the ordered list, when no cursor is given.</param>
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
)
{
    /// <summary>
    /// Only entries whose message contains this text, ignoring case. <c>%</c>, <c>_</c> and
    /// <c>\</c> match themselves. Null or empty matches every entry.
    /// </summary>
    /// <remarks>
    /// Case is folded by the database: on Postgres by the database's collation, on Sqlite for
    /// ASCII letters only. On Postgres a trigram index over the lowered text serves a term of
    /// three characters or more, so a term that matches rarely is found from the index; a term
    /// most entries carry reads in id order and fills a page quickly, but its exact count
    /// (<see cref="IOperationsService.CountLogsAsync"/>) reads every entry it matches, which is why
    /// <see cref="IOperationsService.CountLogsCappedAsync"/> stops at 10,000. On Sqlite, and for a
    /// shorter term, nothing indexes a match inside the text, so a rare term reads far before it
    /// fills a page, and its count, capped or not, reads every row the other filters leave.
    /// </remarks>
    public string? MessageContains { get; init; }

    /// <summary>
    /// Only entries whose category contains this text, ignoring case, matched as
    /// <see cref="MessageContains"/> is. Null or empty matches every entry.
    /// </summary>
    public string? CategoryContains { get; init; }

    /// <summary>
    /// The order entries are read in, and the direction <see cref="AfterId"/> pages in. Newest
    /// first by default.
    /// </summary>
    public LogOrder Order { get; init; } = LogOrder.NewestFirst;
}

/// <summary>The order <see cref="IOperationsService.GetLogsAsync"/> reads log entries in.</summary>
public enum LogOrder
{
    /// <summary>Highest id first: the newest entry first.</summary>
    NewestFirst = 0,

    /// <summary>Lowest id first: the oldest entry first, as a run's log reads.</summary>
    OldestFirst = 1,
}

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

/// <summary>A page of log entries, in the order the query asked for.</summary>
/// <param name="Items">The entries on this page.</param>
/// <param name="Skip">The offset used: 0 when the page was read by cursor.</param>
/// <param name="Take">The page size used, after clamping.</param>
/// <param name="NextCursor">
/// The id of the last entry, to pass as <see cref="LogQuery.AfterId"/> for the next page; null
/// when the page is empty.
/// </param>
public record LogPage(IReadOnlyList<LogRecord> Items, int Skip, int Take, long? NextCursor);

/// <summary>
/// How many log entries match a query, as <see cref="IOperationsService.CountLogsCappedAsync"/>
/// counts them.
/// </summary>
/// <param name="Count">
/// The number of matching entries; when <paramref name="Capped"/> is set, the cap
/// (<see cref="OperationsService.LogCountCap"/>) rather than the number.
/// </param>
/// <param name="Capped">
/// True when more entries match than <see cref="OperationsService.LogCountCap"/>, so the count
/// is a lower bound: "10,000+". Only a query with a text filter is capped.
/// </param>
public record LogCount(int Count, bool Capped);
