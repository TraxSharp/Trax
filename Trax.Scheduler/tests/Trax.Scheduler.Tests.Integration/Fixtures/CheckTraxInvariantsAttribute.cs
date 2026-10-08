using Npgsql;
using NUnit.Framework.Interfaces;
using Trax.Effect.Data.Testing;

[assembly: Trax.Scheduler.Tests.Integration.Fixtures.CheckTraxInvariants]

namespace Trax.Scheduler.Tests.Integration.Fixtures;

/// <summary>
/// Checks the shared test database, and the database of every open Postgres
/// <see cref="InvokeCluster"/>, with <see cref="TraxInvariants"/> after every test in this
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

    public void BeforeTest(ITest test) => _before = Find();

    public void AfterTest(ITest test)
    {
        if (
            test.Fixture is null
            || TraxInvariants.IsExempt(test.Fixture.GetType(), test.MethodName)
        )
            return;

        var added = Find().Except(_before).ToList();
        if (added.Count > 0)
            Assert.Fail(TraxInvariants.Describe(added));
    }

    // ITestAction is synchronous, and the check runs between tests, never inside one.
    private static IReadOnlyList<TraxInvariantViolation> Find() =>
        Find(TestPostgres.ConnectionString, [])
            .Concat(
                InvokeCluster.OpenOnPostgres.SelectMany(c =>
                    Find(c.ConnectionString, c.InvokingStates)
                )
            )
            .ToList();

    // Two databases can hold the same row id, so each violation names the database it is in. The
    // check reads on unpooled connections, so it leaves no idle session in the pool the tests share:
    // DispatcherWakeTests counts sessions in pg_stat_activity on a pooled connection, and missed
    // the listener's session when it was handed one the check had left idle.
    private static IEnumerable<TraxInvariantViolation> Find(
        string pooled,
        IReadOnlyList<InvokingState> invokingStates
    )
    {
        var connectionString = new NpgsqlConnectionStringBuilder(pooled)
        {
            Pooling = false,
        }.ConnectionString;
        return IsMigrated(connectionString)
            ? TraxInvariants
                .FindViolationsAsync(connectionString, invokingStates)
                .GetAwaiter()
                .GetResult()
                .Select(v =>
                    v with
                    {
                        Detail =
                            $"{v.Detail} (in {new NpgsqlConnectionStringBuilder(connectionString).Database})",
                    }
                )
            : [];
    }

    // The first host to start migrates a database, so before it nothing can be stuck. With no
    // server there is nothing to check: the tests that need one fail on their own, and the rest
    // still run (see ExclusiveTestDatabase).
    private static bool IsMigrated(string connectionString)
    {
        using var connection = new NpgsqlConnection(connectionString);
        try
        {
            connection.Open();
        }
        catch (NpgsqlException)
        {
            return false;
        }

        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT to_regclass('trax.work_queue') IS NOT NULL AND to_regclass('trax.effect_claim') IS NOT NULL";
        return (bool)command.ExecuteScalar()!;
    }
}
