using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// <see cref="ISqlDialect.EstimateRowCount"/> on Postgres reads the planner's
/// <c>pg_class.reltuples</c> for a table in the <c>trax</c> schema, and yields no row when there
/// is no estimate to give.
/// </summary>
/// <remarks>
/// Each test works on a table of its own, created inside a transaction that is rolled back, so
/// nothing it does is seen by another suite sharing the database.
/// </remarks>
[TestFixture]
public class PostgresRowCountEstimateTests : TestSetup
{
    private ISqlDialect Dialect => Scope.ServiceProvider.GetRequiredService<ISqlDialect>();

    [Test]
    public async Task An_analyzed_table_reports_its_row_count()
    {
        using var context = (DbContext)DataContextFactory.Create();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var table = await CreateTable(context, rows: 4321);
        await Execute(context, $"ANALYZE trax.{table}");

        var estimate = await Estimate(context, table);

        estimate
            .Should()
            .Equal([4321L], "ANALYZE of a table this small reads every row, so reltuples is exact");
    }

    [Test]
    public async Task A_table_never_analyzed_has_no_estimate()
    {
        using var context = (DbContext)DataContextFactory.Create();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var table = await CreateTable(context, rows: 10);

        var estimate = await Estimate(context, table);

        estimate
            .Should()
            .BeEmpty(
                "reltuples is -1 until the table is first analyzed or vacuumed, which is "
                    + "\"unknown\", not a count"
            );
    }

    [Test]
    public async Task A_missing_table_has_no_estimate()
    {
        using var context = (DbContext)DataContextFactory.Create();

        var estimate = await Estimate(context, $"missing_{Guid.NewGuid():N}");

        estimate.Should().BeEmpty();
    }

    [Test]
    public async Task Only_the_trax_schema_is_read()
    {
        using var context = (DbContext)DataContextFactory.Create();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var table = $"estimate_probe_{Guid.NewGuid():N}";
        await Execute(
            context,
            $"CREATE TABLE public.{table} AS SELECT g FROM generate_series(1, 50) AS g; "
                + $"ANALYZE public.{table};"
        );

        var estimate = await Estimate(context, table);

        estimate.Should().BeEmpty("the Trax tables live in the trax schema and only there");
    }

    private Task<List<long>> Estimate(DbContext context, string table) =>
        context.Database.SqlQueryRaw<long>(Dialect.EstimateRowCount()!, table).ToListAsync();

    private static async Task<string> CreateTable(DbContext context, int rows)
    {
        var table = $"estimate_probe_{Guid.NewGuid():N}";
        await Execute(
            context,
            $"CREATE TABLE trax.{table} AS SELECT g FROM generate_series(1, {rows}) AS g"
        );
        return table;
    }

    /// <summary>
    /// Runs DDL whose table name the test generated itself. A table name cannot be a parameter.
    /// </summary>
    private static Task<int> Execute(DbContext context, string sql) =>
        context.Database.ExecuteSqlRawAsync(sql);
}
