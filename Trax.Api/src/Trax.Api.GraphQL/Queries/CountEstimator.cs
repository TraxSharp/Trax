using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.SqlDialect;

namespace Trax.Api.GraphQL.Queries;

/// <summary>
/// The total for an unfiltered list of a large table: the database's own row estimate when the
/// provider keeps one and the table is large, otherwise an exact count.
/// </summary>
/// <remarks>
/// The estimate comes from <see cref="ISqlDialect.EstimateRowCount"/>, which on Postgres reads
/// <c>pg_class.reltuples</c> (the planner's estimate, refreshed by <c>ANALYZE</c>, <c>VACUUM</c> and
/// autovacuum). An exact <c>COUNT(*)</c> of a table with millions of rows scans all of it, which is
/// not worth paying for a number a pager shows to a person. A provider with no estimate (Sqlite,
/// and InMemory, which registers no dialect at all) counts exactly, as does a table the database
/// has no estimate for yet or one below <see cref="EstimateThreshold"/> rows, where an exact count
/// is cheap and an estimate would visibly disagree with the rows on screen.
/// </remarks>
internal static class CountEstimator
{
    /// <summary>Below this many estimated rows the count is exact.</summary>
    internal const int EstimateThreshold = 10_000;

    /// <summary>
    /// Returns the total and whether it is an estimate.
    /// </summary>
    /// <param name="db">The context the estimate is read through.</param>
    /// <param name="dialect">The provider's dialect, or null when none is registered.</param>
    /// <param name="tableName">The table's unqualified name in the <c>trax</c> schema.</param>
    /// <param name="exactCountAsync">The exact count, used whenever there is no usable estimate.</param>
    /// <param name="ct">Cancels the read.</param>
    public static async Task<(int Count, bool IsEstimate)> EstimateOrCountAsync(
        IDataContext db,
        ISqlDialect? dialect,
        string tableName,
        Func<Task<int>> exactCountAsync,
        CancellationToken ct
    )
    {
        var sql = dialect?.EstimateRowCount();
        if (sql is not null)
        {
            var estimates = await ((DbContext)db)
                .Database.SqlQueryRaw<long>(sql, tableName)
                .ToListAsync(ct);

            if (estimates is [var estimate] && estimate >= EstimateThreshold)
                return ((int)Math.Min(estimate, int.MaxValue), true);
        }

        return (await exactCountAsync(), false);
    }
}
