using Radzen;
using Trax.Dashboard.Models;
using Trax.Scheduler.Services.Operations;

namespace Trax.Dashboard.Utilities;

/// <summary>
/// How the log grids read a page: through <see cref="IOperationsService.GetLogsAsync"/> and
/// <see cref="IOperationsService.CountLogsCappedAsync"/>, the calls the API's logs query makes,
/// with the filter the grid's own filter bar sets. Kept here rather than in the grid component so the
/// stress suite times the reads the grid makes.
/// </summary>
internal static class LogGridQuery
{
    /// <summary>
    /// Reads the page <paramref name="args"/> asks for. Only its skip, take and the Id column's
    /// sort are read: the grid offers no column filter, because the service serves only the
    /// filters in <paramref name="filter"/>.
    /// </summary>
    /// <param name="operations">The operations service.</param>
    /// <param name="filter">
    /// The filter: run, minimum level and the two text filters. Its paging fields and order are
    /// ignored; they come from <paramref name="args"/>.
    /// </param>
    /// <param name="args">The grid's page and sort.</param>
    /// <param name="defaultOrder">The order when the Id column is not sorted.</param>
    /// <param name="count">The grid's remembered total; see <see cref="GridCount"/>.</param>
    /// <param name="capped">
    /// Set when the total was last counted: whether more entries match a text filter than the
    /// service counts (<see cref="OperationsService.LogCountCap"/>), so the total is a lower bound
    /// and the pager reaches only that many.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public static Task<ServerDataResult<LogRow>> LoadPageAsync(
        IOperationsService operations,
        LogQuery filter,
        LoadDataArgs args,
        LogOrder defaultOrder,
        GridCount count,
        LogCountCapped capped,
        CancellationToken ct
    )
    {
        // The count ignores the paging fields and the order, so they are left as given.
        filter = filter with
        {
            MessageContains = NullIfBlank(filter.MessageContains),
            CategoryContains = NullIfBlank(filter.CategoryContains),
        };
        var page = filter with
        {
            AfterId = null,
            Skip = args.Skip ?? 0,
            Take = args.Top ?? 20,
            Order = OrderOf(args, defaultOrder),
        };

        return DataGridQueryHelper.LoadPageAsync<LogRow>(
            async token =>
                (await operations.GetLogsAsync(page, token)).Items.Select(LogRow.From).ToList(),
            async token =>
            {
                var counted = await operations.CountLogsCappedAsync(filter, token);
                capped.Value = counted.Capped;
                return counted.Count;
            },
            string.Join(
                '\u001f',
                filter.MetadataId,
                filter.MinimumLevel,
                filter.Category,
                filter.CategoryContains,
                filter.MessageContains
            ),
            args,
            count,
            ct
        );
    }

    /// <summary>The order the Id column's sort asks for, or <paramref name="defaultOrder"/>.</summary>
    public static LogOrder OrderOf(LoadDataArgs args, LogOrder defaultOrder) =>
        args
            .Sorts?.FirstOrDefault(s => s.Property == nameof(LogRow.Id) && s.SortOrder is not null)
            ?.SortOrder switch
        {
            SortOrder.Ascending => LogOrder.OldestFirst,
            SortOrder.Descending => LogOrder.NewestFirst,
            _ => defaultOrder,
        };

    private static string? NullIfBlank(string? text) =>
        string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}

/// <summary>
/// Whether a log grid's total is the service's cap rather than the number of matching entries.
/// </summary>
internal sealed class LogCountCapped
{
    public bool Value { get; set; }
}
