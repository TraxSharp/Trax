using System.Data.Common;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Tests.Data.Sqlite.Integration.Fixtures;

namespace Trax.Effect.Tests.Data.Sqlite.Integration.IntegrationTests;

/// <summary>
/// The SQLite half of the queued-subject index (migration 011): the "queued behind" lookup for a
/// work queue entry's detail reaches one subject's queued rows through
/// <c>ix_work_queue_subject_queued</c> rather than walking every queued row.
/// </summary>
/// <remarks>
/// SQLite stores the status as its integer, so queued is <c>0</c>. The partial index leaves out
/// null keys; SQLite accepts it for a query whose <c>subject_key = ?</c> term implies the index's
/// <c>subject_key IS NOT NULL</c>, which is the property this pins.
/// </remarks>
public class SqliteWorkQueueSubjectIndexTests : TestSetup
{
    public override ServiceProvider ConfigureServices(IServiceCollection services) =>
        services.BuildServiceProvider();

    [Test]
    public async Task The_queued_behind_lookup_reads_the_queued_subject_index()
    {
        var factory = Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>();
        using var context = (DbContext)await factory.CreateDbContextAsync(CancellationToken.None);

        await context.Database.ExecuteSqlRawAsync(
            """
            WITH RECURSIVE g(n) AS (SELECT 1 UNION ALL SELECT n + 1 FROM g WHERE n < 20000)
            INSERT INTO work_queue
                (external_id, train_name, input_type_name, status, priority, created_at,
                 confirmed_at, subject_key, dispatch_attempts)
            SELECT 'subject-index-' || n, 'Subject.Index', 'Subject.Input', 0, n % 3,
                   datetime('now', '-' || n || ' seconds'), datetime('now'),
                   CASE WHEN n % 20 = 0 THEN 'customer-' || (n / 20 % 200) END, 0
            FROM g;
            ANALYZE;
            """
        );

        var connection = context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            EXPLAIN QUERY PLAN
            SELECT w.id
            FROM work_queue AS w
            LEFT JOIN manifest AS m ON w.manifest_id = m.id
            LEFT JOIN manifest_group AS g ON m.manifest_group_id = g.id
            WHERE w.subject_key = $subject
              AND w.id <> $id
              AND w.status = 0
              AND w.confirmed_at IS NOT NULL
              AND (w.scheduled_at IS NULL OR w.scheduled_at <= $now)
              AND (w.manifest_id IS NULL OR g.is_enabled)
              AND (w.priority > $priority OR (w.priority = $priority AND w.created_at < $now))
            ORDER BY w.priority DESC, w.created_at
            LIMIT 1
            """;
        AddParameter(command, "$subject", "customer-42");
        AddParameter(command, "$id", 0L);
        AddParameter(command, "$now", DateTime.UtcNow);
        AddParameter(command, "$priority", 1);

        // EXPLAIN QUERY PLAN returns (id, parent, notused, detail); detail names the index.
        var plan = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                plan.Add(reader.GetString(3));

        string.Join('\n', plan)
            .Should()
            .Contain(
                "ix_work_queue_subject_queued",
                "one subject's queued rows are a handful out of the backlog. The plan was:\n"
                    + string.Join('\n', plan)
            );
    }

    private static void AddParameter(DbCommand command, string name, object value)
    {
        var parameter = command.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value;
        command.Parameters.Add(parameter);
    }
}
