using Microsoft.Extensions.Configuration;
using Npgsql;
using NUnit.Framework.Interfaces;
using Trax.Effect.Data.Testing;

[assembly: Trax.Mediator.Tests.Postgres.Integration.Fixtures.CheckTraxInvariants]

namespace Trax.Mediator.Tests.Postgres.Integration.Fixtures;

/// <summary>
/// Checks the shared test database with <see cref="TraxInvariants"/> after every test in this
/// assembly, once its teardown has run and the hosts it started are disposed.
/// </summary>
/// <remarks>
/// <para>It wraps every test rather than living in <see cref="TestSetup"/>'s teardown, so it runs
/// after every teardown, including those of the fixtures that build a host of their own on top of
/// that base, and so after those hosts are disposed.</para>
/// <para><see cref="TestSetup"/> empties the run and queue tables before each test but not the
/// effect claims, so the check compares against what was there when the test began and reports
/// only the violations this test added. A test that leaves one on purpose carries
/// <see cref="LeavesStuckRunsAttribute"/>.</para>
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly)]
public sealed class CheckTraxInvariantsAttribute : Attribute, ITestAction
{
    private static readonly Lazy<string> ConnectionString = new(() =>
        TestPostgres.WithPort(
            new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.json", optional: false)
                .Build()
                .GetRequiredSection("Configuration")["DatabaseConnectionString"]!
        )
    );

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
        IsMigrated()
            ? TraxInvariants.FindViolationsAsync(ConnectionString.Value).GetAwaiter().GetResult()
            : [];

    // The first host to start migrates the shared database, so before it nothing can be stuck.
    private static bool IsMigrated()
    {
        using var connection = new NpgsqlConnection(ConnectionString.Value);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            "SELECT to_regclass('trax.work_queue') IS NOT NULL AND to_regclass('trax.effect_claim') IS NOT NULL";
        return (bool)command.ExecuteScalar()!;
    }
}
