using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// The operator listing of state-machine instances reads newest first by <c>updated_at</c>. An
/// index on <c>(machine, state)</c> alone finds a state's rows but not in that order, so a page
/// of a busy state sorted every row in it. These check the planner reads the indexes Trax.Effect's
/// Postgres migration 071 adds, in the listing's order, for the query
/// <see cref="OperationsService.GetMachineInstancesAsync"/> actually runs.
/// </summary>
/// <remarks>
/// The rows are seeded in one transaction that is rolled back, so no other suite sharing the
/// database sees them. <c>ANALYZE</c> counts the transaction's own uncommitted rows.
/// </remarks>
[TestFixture]
[NonParallelizable]
public class MachineInstanceListingIndexTests : TestSetup
{
    [Test]
    public async Task A_page_of_one_machine_and_state_reads_their_index_in_order()
    {
        var plan = await PlanOf(
            new MachineInstanceQuery("index-machine-1", "State3", SnapshotOwnerKind.System)
        );

        plan.Should()
            .Contain(
                "ix_snapshot_draft_machine_state_updated",
                "a page of one state should come from that state's rows in updated_at order. "
                    + $"The plan was:\n{plan}"
            )
            .And.NotContain("Sort", $"the index holds the rows in the listing's order:\n{plan}");
    }

    [Test]
    public async Task An_unfiltered_page_reads_the_updated_index_and_stops_at_the_page()
    {
        var plan = await PlanOf(new MachineInstanceQuery());

        plan.Should()
            .Contain(
                "ix_snapshot_draft_updated",
                "the first page of every instance should be read from the newest end of the "
                    + $"updated_at index, not by sorting the table. The plan was:\n{plan}"
            )
            .And.NotContain("Sort", $"the index holds the rows in the listing's order:\n{plan}");
    }

    [Test]
    public async Task A_stuck_page_finds_each_token_run_through_the_external_id_index()
    {
        // metadata.external_id is character(32) and invoke_token is text: compared as they are, Postgres casts the
        // run's column and reads every run for every row holding a token.
        var plan = await PlanOf(new MachineInstanceQuery { Stuck = true }, withRuns: true);

        plan.Should()
            .Contain(
                "ix_metadata_external_id",
                $"each token's run should be looked up through the run's external-id index. The plan was:\n{plan}"
            )
            .And.NotContain(
                "Seq Scan on metadata",
                $"no lookup of a token's run should read the whole run table:\n{plan}"
            );
    }

    private async Task<string> PlanOf(MachineInstanceQuery query, bool withRuns = false)
    {
        await using var context = (DbContext)
            Scope.ServiceProvider.GetRequiredService<IDataContextProviderFactory>().Create();
        await context.Database.OpenConnectionAsync();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var connection = (NpgsqlConnection)context.Database.GetDbConnection();
        var npgsqlTransaction = (NpgsqlTransaction)transaction.GetDbTransaction();

        // 40,000 instances over four machines and ten states, written over the last day.
        await Execute(
            connection,
            npgsqlTransaction,
            """
            INSERT INTO trax.snapshot_draft (id, user_key, owner_kind, machine, version, state, context, concurrency_token, created_at, updated_at)
            SELECT gen_random_uuid(),
                   CASE WHEN g % 3 = 0 THEN 'user-' || g ELSE NULL END,
                   CASE WHEN g % 3 = 0 THEN 'user'::trax.snapshot_owner_kind ELSE 'system'::trax.snapshot_owner_kind END,
                   'index-machine-' || (g % 4),
                   1,
                   'State' || (g % 10),
                   '{}',
                   gen_random_uuid(),
                   now() - (g || ' seconds')::interval,
                   now() - ((g % 86400) || ' seconds')::interval
            FROM generate_series(1, 40000) AS g;

            ANALYZE trax.snapshot_draft;
            """
        );
        if (withRuns)
            // One instance in a hundred waits on a run. A run table this small fits in one hash, which the planner
            // reads once for every token; a real one does not, and then each token's run is looked up on its own.
            // The small work_mem makes the planner plan for the real table.
            await Execute(
                connection,
                npgsqlTransaction,
                """
                UPDATE trax.snapshot_draft SET invoke_token = md5(row_id::text)
                WHERE machine LIKE 'index-machine-%' AND row_id % 100 = 0;

                INSERT INTO trax.metadata (external_id, name, train_state, start_time, end_time)
                SELECT md5(g::text),
                       'index-train',
                       (ARRAY['completed','failed','cancelled']::trax.train_state[])[1 + (g % 3)],
                       now() - interval '1 hour',
                       now() - interval '1 hour'
                FROM generate_series(1, 40000) AS g;

                ANALYZE trax.snapshot_draft;
                ANALYZE trax.metadata;

                SET LOCAL work_mem = '64kB';
                """
            );

        var page = OperationsService.MachineInstancePageQuery(
            (IDataContext)context,
            query,
            skip: 0,
            take: 25
        );
        await using var command = (NpgsqlCommand)page.CreateDbCommand();
        command.Transaction = npgsqlTransaction;
        command.CommandText = "EXPLAIN " + command.CommandText;
        var plan = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
                plan.Add(reader.GetString(0));

        await transaction.RollbackAsync();
        return string.Join('\n', plan);
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
}
