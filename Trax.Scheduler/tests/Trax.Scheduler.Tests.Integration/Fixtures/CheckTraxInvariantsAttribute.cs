using Microsoft.Data.Sqlite;
using Npgsql;
using NUnit.Framework.Interfaces;
using Trax.Core.Extensions;
using Trax.Effect.Data.Testing;

[assembly: Trax.Scheduler.Tests.Integration.Fixtures.CheckTraxInvariants]

namespace Trax.Scheduler.Tests.Integration.Fixtures;

/// <summary>
/// Checks the shared test database, and the database of every open
/// <see cref="InvokeCluster"/> on Postgres or SQLite, with <see cref="TraxInvariants"/> after every test in this
/// assembly, once its teardown has run and the hosts it started are disposed.
/// </summary>
/// <remarks>
/// <para>It wraps every test rather than living in <see cref="TestSetup"/>, because most of the
/// tests that start a host of their own do not derive that base, and they are the ones most likely
/// to leave a run behind.</para>
/// <para><see cref="TestSetup"/> and <see cref="InvokeCluster.Reset"/> empty the tables before
/// each of their tests and the other tests do not, so the check compares against what was there
/// when the test began and reports only the violations this test added. A test that leaves one on
/// purpose carries <see cref="LeavesStuckRunsAttribute"/>.</para>
/// <para>A cluster's hosts live for its whole fixture, but nothing in them runs in the background:
/// every dispatch, run and sweep is a call the test made and awaited, so between tests they are as
/// still as stopped ones. A cluster's database is also checked against the invoking states of the
/// machines its hosts register. The databases the builder tests in <c>UnitTests/</c> name are
/// never written, and are not checked.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class CheckTraxInvariantsAttribute : Attribute, ITestAction
{
    private IReadOnlyList<TraxInvariantViolation> _before = [];

    public ActionTargets Targets => ActionTargets.Test;

    // ITestAction is synchronous, and the check runs between tests, never inside one, so it
    // waits here. Every await below is ConfigureAwait(false).
    public void BeforeTest(ITest test) => _before = FindAsync().GetAwaiter().GetResult();

    public void AfterTest(ITest test)
    {
        if (
            test.Fixture is null
            || TraxInvariants.IsExempt(test.Fixture.GetType(), test.MethodName)
        )
            return;

        var added = FindAsync().GetAwaiter().GetResult().Except(_before).ToList();
        if (added.Count > 0)
            Assert.Fail(TraxInvariants.Describe(added));
    }

    private static async Task<IReadOnlyList<TraxInvariantViolation>> FindAsync()
    {
        var found = new List<TraxInvariantViolation>();
        found.AddRange(await FindAsync(TestPostgres.ConnectionString, []).ConfigureAwait(false));
        foreach (var cluster in InvokeCluster.OpenClusters)
            found.AddRange(
                cluster.Store == ClusterStore.Postgres
                    ? await FindAsync(cluster.ConnectionString, cluster.InvokingStates)
                        .ConfigureAwait(false)
                    : await FindOnSqliteAsync(cluster.ConnectionString, cluster.InvokingStates)
                        .ConfigureAwait(false)
            );
        return found;
    }

    // A SQLite cluster's file. Read on an unpooled connection that may not create the file, so the
    // check neither holds the file open nor makes one before the cluster's first host has.
    private static async Task<IReadOnlyList<TraxInvariantViolation>> FindOnSqliteAsync(
        string connectionString,
        IReadOnlyList<InvokingState> invokingStates
    )
    {
        var file = new SqliteConnectionStringBuilder(connectionString).DataSource;
        var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = file,
                Mode = SqliteOpenMode.ReadWrite,
                Pooling = false,
            }.ConnectionString
        );
        await using var connectionDisposal = connection.ConfigureAwait(false);
        try
        {
            await connection.OpenAsync().ConfigureAwait(false);
        }
        catch (SqliteException)
        {
            return [];
        }

        var migrated = connection.CreateCommand();
        await using (migrated.ConfigureAwait(false))
        {
            migrated.CommandText =
                "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name IN ('work_queue', 'effect_claim')";
            if ((long)(await migrated.ExecuteScalarAsync().ConfigureAwait(false)).NotNull() < 2)
                return [];
        }

        return (
            await TraxInvariants
                .FindViolationsAsync(connection, invokingStates)
                .ConfigureAwait(false)
        )
            .Select(v => v with { Detail = $"{v.Detail} (in {Path.GetFileName(file)})" })
            .ToList();
    }

    // Two databases can hold the same row id, so each violation names the database it is in. The
    // check reads on unpooled connections, so it leaves no idle session in the pool the tests share:
    // DispatcherWakeTests counts sessions in pg_stat_activity on a pooled connection, and missed
    // the listener's session when it was handed one the check had left idle.
    private static async Task<IReadOnlyList<TraxInvariantViolation>> FindAsync(
        string pooled,
        IReadOnlyList<InvokingState> invokingStates
    )
    {
        var connectionString = new NpgsqlConnectionStringBuilder(pooled)
        {
            Pooling = false,
        }.ConnectionString;
        if (!await IsMigratedAsync(connectionString).ConfigureAwait(false))
            return [];

        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
        return (
            await TraxInvariants
                .FindViolationsAsync(connectionString, invokingStates)
                .ConfigureAwait(false)
        )
            .Select(v => v with { Detail = $"{v.Detail} (in {database})" })
            .ToList();
    }

    // The first host to start migrates a database, so before it nothing can be stuck. With no
    // server there is nothing to check: the tests that need one fail on their own, and the rest
    // still run (see ExclusiveTestDatabase).
    private static async Task<bool> IsMigratedAsync(string connectionString)
    {
        var connection = new NpgsqlConnection(connectionString);
        await using var connectionDisposal = connection.ConfigureAwait(false);
        try
        {
            await connection.OpenAsync().ConfigureAwait(false);
        }
        catch (NpgsqlException)
        {
            return false;
        }

        var command = connection.CreateCommand();
        await using var commandDisposal = command.ConfigureAwait(false);
        command.CommandText =
            "SELECT to_regclass('trax.work_queue') IS NOT NULL AND to_regclass('trax.effect_claim') IS NOT NULL";
        return (bool)(await command.ExecuteScalarAsync().ConfigureAwait(false)).NotNull();
    }
}
