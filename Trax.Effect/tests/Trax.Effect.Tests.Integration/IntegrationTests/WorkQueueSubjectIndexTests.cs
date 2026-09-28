using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// A work queue entry's detail says which queued sibling for the same subject dispatch would take
/// before it. That lookup asks for the queued rows of one subject, and without an index over them
/// it reads every queued row in the table. Migration 046 adds <c>ix_work_queue_subject_queued</c>
/// for it; this checks the planner picks it on a table shaped like a real backlog.
/// </summary>
/// <remarks>
/// The statement mirrors the "queued behind" predicate the Api's work queue detail and the Blazor
/// dashboard's detail page both run: same subject, queued, confirmed, due, in an enabled group or
/// none, and ahead of the entry by priority then age.
///
/// Everything happens inside one transaction that is rolled back, so the seeded backlog is never
/// visible to another suite sharing the database. <c>ANALYZE</c> counts the transaction's own
/// uncommitted rows, which is what gives the planner a realistic picture of them.
/// </remarks>
[TestFixture]
[NonParallelizable]
public class WorkQueueSubjectIndexTests : TestSetup
{
    private const string QueuedBehindLookup = """
        EXPLAIN
        SELECT w.id
        FROM trax.work_queue AS w
        LEFT JOIN trax.manifest AS m ON w.manifest_id = m.id
        LEFT JOIN trax.manifest_group AS g ON m.manifest_group_id = g.id
        WHERE w.subject_key = @subject
          AND w.id <> @id
          AND w.status = 'queued'
          AND w.confirmed_at IS NOT NULL
          AND (w.scheduled_at IS NULL OR w.scheduled_at <= @now)
          AND (w.manifest_id IS NULL OR g.is_enabled)
          AND (w.priority > @priority OR (w.priority = @priority AND w.created_at < @created))
        ORDER BY w.priority DESC, w.created_at
        LIMIT 1
        """;

    [Test]
    public async Task The_queued_behind_lookup_reads_the_queued_subject_index()
    {
        using var context = (DbContext)DataContextFactory.Create();
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        // 60,000 queued entries, one in twenty carrying one of 600 subjects, and 20,000 dispatched
        // entries for the same subjects: a backlog mostly of manifest work, as a real one is.
        await Execute(
            connection,
            """
            INSERT INTO trax.work_queue
                (external_id, train_name, input_type_name, status, priority, created_at,
                 confirmed_at, subject_key)
            SELECT gen_random_uuid()::text, 'Subject.Index', 'Subject.Input', 'queued', g % 3,
                   (now() AT TIME ZONE 'utc') - make_interval(secs => g), now(),
                   CASE WHEN g % 20 = 0 THEN 'customer-' || (g / 20 % 600) END
            FROM generate_series(1, 60000) AS g;

            INSERT INTO trax.work_queue
                (external_id, train_name, input_type_name, status, priority, created_at,
                 confirmed_at, subject_key)
            SELECT gen_random_uuid()::text, 'Subject.Index', 'Subject.Input', 'dispatched', 0,
                   (now() AT TIME ZONE 'utc') - make_interval(secs => g), now(),
                   'customer-' || (g % 600)
            FROM generate_series(1, 20000) AS g;

            ANALYZE trax.work_queue;
            """
        );

        var plan = await Explain(connection, subject: "customer-42");

        plan.Should()
            .Contain(
                "ix_work_queue_subject_queued",
                "one subject's queued rows are a handful out of the whole backlog, so the lookup "
                    + $"should reach them through the partial index. The plan was:\n{plan}"
            );

        await transaction.RollbackAsync();
    }

    private static async Task Execute(DbConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> Explain(NpgsqlConnection connection, string subject)
    {
        await using var command = new NpgsqlCommand(QueuedBehindLookup, connection);
        command.Parameters.AddWithValue("subject", subject);
        command.Parameters.AddWithValue("id", 0L);
        command.Parameters.AddWithValue("now", DateTime.UtcNow);
        command.Parameters.AddWithValue("priority", (short)1);
        command.Parameters.AddWithValue(
            "created",
            DateTime.SpecifyKind(DateTime.UtcNow, DateTimeKind.Unspecified)
        );

        var lines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            lines.Add(reader.GetString(0));

        return string.Join('\n', lines);
    }
}
