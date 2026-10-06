using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Tests.Integration.Fixtures;

namespace Trax.Effect.Tests.Integration.IntegrationTests;

/// <summary>
/// A run's recorded decisions are read a page at a time in id order. The unique index on
/// <c>(metadata_id, question_key, occurrence)</c> finds a run's rows but not in that order, so
/// without migration 064 the planner walks the primary key and filters on the run, reading past
/// every other run's decisions first. This checks the planner takes
/// <c>ix_decision_metadata_id_id</c> for the page query on a table shaped like a real history.
/// </summary>
/// <remarks>
/// The rows are seeded in one transaction that is rolled back, so no other suite sharing the
/// database sees them. <c>ANALYZE</c> counts the transaction's own uncommitted rows.
/// </remarks>
[TestFixture]
[NonParallelizable]
public class DecisionPageIndexTests : TestSetup
{
    [Test]
    public async Task A_runs_first_page_of_decisions_reads_the_run_and_id_index()
    {
        using var context = (DbContext)DataContextFactory.Create();
        await context.Database.OpenConnectionAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var npgsqlTransaction = (NpgsqlTransaction)transaction.GetDbTransaction();

        // 20,000 runs of four decisions each, then one run of 200: the large run is the newest, so
        // in id order its rows come after everything else. The planner prices a walk of the
        // primary key as if the run's rows were spread evenly through it, so the walk's estimate
        // falls as the run grows; at 2,000 rows it tied with the index on a freshly loaded table
        // and the choice turned on ANALYZE's sample. At 200 the index wins by several times.
        await Execute(
            connection,
            npgsqlTransaction,
            """
            INSERT INTO trax.metadata (external_id, name, train_state, start_time)
            SELECT md5(g::text), 'Decision.Page', 'completed', now()
            FROM generate_series(1, 20001) AS g;

            INSERT INTO trax.decision (metadata_id, question_key, occurrence, fingerprint, kind, question, answer)
            SELECT m.id, 'q' || o, 1, 'f', 'model', '{}', '{}'
            FROM trax.metadata AS m CROSS JOIN generate_series(1, 4) AS o
            WHERE m.name = 'Decision.Page' AND m.external_id <> md5('20001');

            INSERT INTO trax.decision (metadata_id, question_key, occurrence, fingerprint, kind, question, answer)
            SELECT m.id, 'q', o, 'f', 'model', '{}', '{}'
            FROM trax.metadata AS m CROSS JOIN generate_series(1, 200) AS o
            WHERE m.external_id = md5('20001');

            ANALYZE trax.metadata;
            ANALYZE trax.decision;
            """
        );
        var runId = await Scalar(
            connection,
            npgsqlTransaction,
            "SELECT id FROM trax.metadata WHERE external_id = md5('20001')"
        );

        var page = ((IDataContext)context)
            .RecordedDecisions.AsNoTracking()
            .Where(d => d.MetadataId == runId)
            .OrderBy(d => d.Id)
            .Take(26);
        await using var command = (NpgsqlCommand)page.CreateDbCommand();
        command.Transaction = npgsqlTransaction;
        command.CommandText = "EXPLAIN " + command.CommandText;
        var plan = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                plan.Add(reader.GetString(0));

        string.Join('\n', plan)
            .Should()
            .Contain(
                "ix_decision_metadata_id_id",
                "a run's first page should come from its own rows in id order, not a walk of the "
                    + $"primary key past every other run's. The plan was:\n{string.Join('\n', plan)}"
            );

        await transaction.RollbackAsync();
    }

    private static async Task Execute(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql
    )
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<long> Scalar(
        NpgsqlConnection connection,
        NpgsqlTransaction transaction,
        string sql
    )
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        return (long)(await command.ExecuteScalarAsync())!;
    }
}
