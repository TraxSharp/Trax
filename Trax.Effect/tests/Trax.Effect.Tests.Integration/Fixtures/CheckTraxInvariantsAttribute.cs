using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;
using Npgsql;
using NUnit.Framework.Interfaces;
using Trax.Core.Extensions;
using Trax.Effect.Data.Testing;

[assembly: Trax.Effect.Tests.Integration.Fixtures.CheckTraxInvariants]

namespace Trax.Effect.Tests.Integration.Fixtures;

/// <summary>
/// Checks the shared test database, and the file of every open SQLite
/// <see cref="CheckpointHost"/>, with <see cref="TraxInvariants"/> after every test in this
/// assembly, once its teardown has run and the hosts it started are disposed.
/// </summary>
/// <remarks>
/// <para>It wraps every test rather than living in <see cref="TestSetup"/>, because most of the
/// tests that start a host of their own do not derive that base, and they are the ones most likely
/// to leave a run behind.</para>
/// <para><see cref="TestSetup"/> empties the tables before each of its tests and the other tests do
/// not, so the check compares against what was there when the test began and reports only the
/// violations this test added. A test that leaves one on purpose carries
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
                .GetRequiredSection("Configuration")["DatabaseConnectionString"]
                .NotNull()
        )
    );

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
        if (await IsMigratedAsync().ConfigureAwait(false))
            found.AddRange(
                await TraxInvariants
                    .FindViolationsAsync(ConnectionString.Value)
                    .ConfigureAwait(false)
            );
        foreach (var file in CheckpointHost.OpenOnSqlite)
            found.AddRange(await FindOnSqliteAsync(file).ConfigureAwait(false));
        return found;
    }

    // A SQLite host's file, while its fixture keeps it open. Read on an unpooled connection that
    // may not create the file, so the check neither holds the file open nor makes one that a host
    // has not; each violation names the file, since two files can hold the same row id.
    private static async Task<IReadOnlyList<TraxInvariantViolation>> FindOnSqliteAsync(string file)
    {
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
                "SELECT count(*) FROM sqlite_master WHERE type = 'table' AND name IN ('work_queue', 'checkpoint')";
            if ((long)(await migrated.ExecuteScalarAsync().ConfigureAwait(false)).NotNull() < 2)
                return [];
        }

        return (await TraxInvariants.FindViolationsAsync(connection, []).ConfigureAwait(false))
            .Select(v => v with { Detail = $"{v.Detail} (in {Path.GetFileName(file)})" })
            .ToList();
    }

    // The first host to start migrates the shared database, so before it nothing can be stuck.
    private static async Task<bool> IsMigratedAsync()
    {
        var connection = new NpgsqlConnection(ConnectionString.Value);
        await using var connectionDisposal = connection.ConfigureAwait(false);
        await connection.OpenAsync().ConfigureAwait(false);
        var command = connection.CreateCommand();
        await using var commandDisposal = command.ConfigureAwait(false);
        command.CommandText =
            "SELECT to_regclass('trax.work_queue') IS NOT NULL AND to_regclass('trax.effect_claim') IS NOT NULL";
        return (bool)(await command.ExecuteScalarAsync().ConfigureAwait(false)).NotNull();
    }
}
