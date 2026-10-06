using System.Linq.Dynamic.Core;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Trax.Dashboard.Models;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;

namespace Trax.Dashboard.Utilities;

/// <summary>
/// Eliminates duplicated server-side pagination boilerplate across DataGrid list pages.
/// Each page provides a query factory that returns the base IQueryable with default ordering;
/// this helper applies dynamic filtering, sorting, pagination, and returns the result.
/// </summary>
internal static class DataGridQueryHelper
{
    /// <summary>
    /// Loads a page of rows projected from an entity, so the page reads only the columns its grid
    /// shows. Grid filters and sorts apply to the projected row, and EF Core translates them to
    /// the entity's columns.
    /// </summary>
    /// <param name="factory">The data context factory to create a DbContext.</param>
    /// <param name="queryFactory">The base query with its scope and default ordering.</param>
    /// <param name="projection">The row the grid shows, without the columns it does not.</param>
    /// <param name="args">The Radzen LoadDataArgs containing filter, sort, skip, and take values.</param>
    /// <param name="count">The grid's remembered total; see <see cref="GridCount"/>.</param>
    /// <param name="scope">
    /// What the base query is scoped to beyond the grid's filter (a route id, a page setting), so a
    /// change to it is counted again rather than answered from <paramref name="count"/>.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    public static Task<ServerDataResult<TRow>> LoadPageAsync<TEntity, TRow>(
        IDataContextProviderFactory factory,
        Func<IDataContext, IQueryable<TEntity>> queryFactory,
        Expression<Func<TEntity, TRow>> projection,
        Radzen.LoadDataArgs args,
        GridCount count,
        object? scope,
        CancellationToken ct
    )
        where TRow : class =>
        LoadPageAsync(factory, db => queryFactory(db).Select(projection), args, count, scope, ct);

    /// <summary>
    /// Loads a page of data for a server-side TraxDataGrid.
    /// </summary>
    /// <param name="factory">The data context factory to create a DbContext.</param>
    /// <param name="queryFactory">
    /// A function that receives the IDataContext and returns the base IQueryable
    /// with default ordering applied (e.g. <c>db.WorkQueues.AsNoTracking().OrderByDescending(q => q.Id)</c>).
    /// </param>
    /// <param name="args">The Radzen LoadDataArgs containing filter, sort, skip, and take values.</param>
    /// <param name="count">
    /// The grid's remembered total. Counting every matching row is the expensive half of a page
    /// load on a large table, and a grid reloads its page on every poll tick, so the total is
    /// counted again only when the filter or <paramref name="scope"/> changes, when it is older
    /// than <see cref="GridCount.MaxAge"/>, or when the page shows it is too small. A page shorter
    /// than the page size is the last one, which gives the exact total without counting.
    /// </param>
    /// <param name="scope">What the base query is scoped to beyond the grid's filter.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<ServerDataResult<T>> LoadPageAsync<T>(
        IDataContextProviderFactory factory,
        Func<IDataContext, IQueryable<T>> queryFactory,
        Radzen.LoadDataArgs args,
        GridCount count,
        object? scope,
        CancellationToken ct
    )
        where T : class
    {
        using var context = await factory.CreateDbContextAsync(ct);
        var query = queryFactory(context);

        if (!string.IsNullOrEmpty(args.Filter))
            query = query.Where(args.Filter);

        if (!string.IsNullOrEmpty(args.OrderBy))
            query = query.OrderBy(args.OrderBy);

        var page = query;
        if (args.Skip.HasValue)
            page = page.Skip(args.Skip.Value);
        if (args.Top.HasValue)
            page = page.Take(args.Top.Value);

        return await LoadPageAsync<T>(
            async token => await page.ToListAsync(token),
            token => query.CountAsync(token),
            $"{scope}\u001f{args.Filter}",
            args,
            count,
            ct
        );
    }

    /// <summary>
    /// Loads a page of data for a server-side TraxDataGrid from a service that reads pages and
    /// counts them itself, such as <c>IOperationsService.GetLogsAsync</c> and
    /// <c>CountLogsAsync</c>, remembering the total in <paramref name="count"/> as the query
    /// overload does.
    /// </summary>
    /// <param name="readPage">Reads the page <paramref name="args"/> asks for.</param>
    /// <param name="countAll">Counts every row the filter matches.</param>
    /// <param name="filterKey">
    /// Identifies the filter and scope the rows are counted under, so a change to either is
    /// counted again rather than answered from <paramref name="count"/>.
    /// </param>
    /// <param name="args">The Radzen LoadDataArgs; only its skip and take are read here.</param>
    /// <param name="count">The grid's remembered total; see <see cref="GridCount"/>.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<ServerDataResult<T>> LoadPageAsync<T>(
        Func<CancellationToken, Task<IReadOnlyList<T>>> readPage,
        Func<CancellationToken, Task<int>> countAll,
        string filterKey,
        Radzen.LoadDataArgs args,
        GridCount count,
        CancellationToken ct
    )
        where T : class
    {
        var counted = !count.TryGet(filterKey, out var total);
        if (counted)
            total = await countAll(ct);

        var items = await readPage(ct);

        var skip = args.Skip ?? 0;
        if (args.Top is { } top && items.Count < top && (items.Count > 0 || skip == 0))
        {
            // The last page: the total is exact without counting.
            total = skip + items.Count;
            counted = true;
        }
        else if (total < skip + items.Count)
        {
            // Rows were added since the total was counted, past what it allows for.
            total = await countAll(ct);
            counted = true;
        }

        if (counted)
            count.Set(filterKey, total);

        return new ServerDataResult<T>(items, total);
    }
}

/// <summary>
/// One grid's remembered row total, so a poll tick that reloads the same page does not count
/// the whole table again. It is valid for one filter and scope and for <see cref="MaxAge"/>; the
/// pager's total can lag by at most that long, while the rows on the page are always read fresh.
/// </summary>
internal sealed class GridCount(TimeProvider? clock = null)
{
    /// <summary>How long a counted total is reused for the same filter and scope.</summary>
    public static readonly TimeSpan MaxAge = TimeSpan.FromSeconds(30);

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly Lock _gate = new();
    private string? _key;
    private int _total;
    private DateTimeOffset _countedAt;

    /// <summary>The remembered total for <paramref name="key"/>, if it is still valid.</summary>
    public bool TryGet(string key, out int total)
    {
        lock (_gate)
        {
            total = _total;
            return _key == key && _clock.GetUtcNow() - _countedAt < MaxAge;
        }
    }

    /// <summary>Remembers <paramref name="total"/> for <paramref name="key"/> from now.</summary>
    public void Set(string key, int total)
    {
        lock (_gate)
        {
            _key = key;
            _total = total;
            _countedAt = _clock.GetUtcNow();
        }
    }
}
