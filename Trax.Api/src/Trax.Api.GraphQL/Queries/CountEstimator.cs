using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.DataContext;

namespace Trax.Api.GraphQL.Queries;

/// <summary>
/// Uses PostgreSQL's <c>pg_class.reltuples</c> to estimate row counts for large tables
/// without a full sequential scan. Falls back to exact COUNT(*) for small tables.
/// </summary>
internal static class CountEstimator
{
    private const int EstimateThreshold = 10_000;

    /// <summary>
    /// Returns the estimated row count and whether it is an estimate.
    /// For tables with fewer than <see cref="EstimateThreshold"/> estimated rows,
    /// falls back to an exact count via <paramref name="exactCountAsync"/>.
    /// </summary>
    public static async Task<(int Count, bool IsEstimate)> EstimateOrCountAsync(
        IDataContext db,
        string tableName,
        Func<Task<int>> exactCountAsync,
        CancellationToken ct
    )
    {
        var dbContext = (DbContext)db;
        var connection = dbContext.Database.GetDbConnection();

        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(ct);

        await using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT reltuples::bigint FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE c.relname = @table AND n.nspname = 'trax'";

        var param = command.CreateParameter();
        param.ParameterName = "table";
        param.Value = tableName;
        command.Parameters.Add(param);

        var result = await command.ExecuteScalarAsync(ct);

        if (result is long estimate && estimate >= EstimateThreshold)
            return ((int)Math.Min(estimate, int.MaxValue), true);

        // Below threshold or ANALYZE hasn't run yet — do exact count
        var exactCount = await exactCountAsync();
        return (exactCount, false);
    }
}
