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

    private async Task<string> PlanOf(MachineInstanceQuery query)
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
