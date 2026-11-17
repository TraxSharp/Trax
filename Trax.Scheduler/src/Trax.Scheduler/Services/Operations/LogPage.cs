using Microsoft.Extensions.Logging;

namespace Trax.Scheduler.Services.Operations;

/// <summary>A page of log entries, in the order the query asked for.</summary>
/// <param name="Items">The entries on this page.</param>
/// <param name="Skip">The offset used: 0 when the page was read by cursor.</param>
/// <param name="Take">The page size used, after clamping.</param>
/// <param name="NextCursor">
/// The id of the last entry, to pass as <see cref="LogQuery.AfterId"/> for the next page; null
/// when the page is empty.
/// </param>
public record LogPage(IReadOnlyList<LogRecord> Items, int Skip, int Take, long? NextCursor);
