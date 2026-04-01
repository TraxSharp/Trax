using System.Linq.Dynamic.Core;
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
public static class DataGridQueryHelper
{
    /// <summary>
    /// Loads a page of data for a server-side TraxDataGrid.
    /// </summary>
    /// <param name="factory">The data context factory to create a DbContext.</param>
    /// <param name="queryFactory">
    /// A function that receives the IDataContext and returns the base IQueryable
    /// with default ordering applied (e.g. <c>db.WorkQueues.AsNoTracking().OrderByDescending(q => q.Id)</c>).
    /// </param>
    /// <param name="args">The Radzen LoadDataArgs containing filter, sort, skip, and take values.</param>
    /// <param name="ct">Cancellation token.</param>
    public static async Task<ServerDataResult<T>> LoadPageAsync<T>(
        IDataContextProviderFactory factory,
        Func<IDataContext, IQueryable<T>> queryFactory,
        Radzen.LoadDataArgs args,
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

        var count = await query.CountAsync(ct);

        if (args.Skip.HasValue)
            query = query.Skip(args.Skip.Value);
        if (args.Top.HasValue)
            query = query.Take(args.Top.Value);

        var items = await query.ToListAsync(ct);
        return new ServerDataResult<T>(items, count);
    }
}
