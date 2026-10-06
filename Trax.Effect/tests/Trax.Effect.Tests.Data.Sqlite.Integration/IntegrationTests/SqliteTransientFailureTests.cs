using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Extensions;

namespace Trax.Effect.Tests.Data.Sqlite.Integration.IntegrationTests;

/// <summary>
/// The SQLite dialect reads a busy or locked database as worth retrying, since another
/// connection's write causes it and it clears when that write commits, and nothing else.
/// </summary>
[TestFixture]
public class SqliteTransientFailureTests
{
    private ISqlDialect _dialect = null!;
    private ServiceProvider _provider = null!;
    private string _dbPath = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"trax_transient_{Guid.NewGuid():N}.db");
        _provider = new ServiceCollection()
            .AddTrax(trax =>
                trax.AddEffects(effects => effects.UseSqlite($"Data Source={_dbPath}"))
            )
            .BuildServiceProvider();
        _dialect = _provider.GetRequiredService<ISqlDialect>();
    }

    [OneTimeTearDown]
    public async Task TearDown()
    {
        await _provider.DisposeAsync();
        if (File.Exists(_dbPath))
            File.Delete(_dbPath);
    }

    [TestCase(SQLitePCL.raw.SQLITE_BUSY)]
    [TestCase(SQLitePCL.raw.SQLITE_LOCKED)]
    public void A_busy_or_locked_database_is_transient(int code)
    {
        _dialect.IsTransient(new SqliteException("busy", code)).Should().BeTrue();
        _dialect
            .IsTransient(new DbUpdateException("save failed", new SqliteException("busy", code)))
            .Should()
            .BeTrue();
    }

    [Test]
    public void A_constraint_violation_is_not_transient()
    {
        _dialect
            .IsTransient(
                new SqliteException(
                    "UNIQUE constraint failed",
                    SQLitePCL.raw.SQLITE_CONSTRAINT,
                    SQLitePCL.raw.SQLITE_CONSTRAINT_UNIQUE
                )
            )
            .Should()
            .BeFalse();
    }

    [Test]
    public void A_real_syntax_error_is_not_transient()
    {
        using var connection = new SqliteConnection($"Data Source={_dbPath}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELEKT 1";

        var failure = FluentActions
            .Invoking(() => command.ExecuteNonQuery())
            .Should()
            .Throw<SqliteException>()
            .Which;

        _dialect.IsTransient(failure).Should().BeFalse();
    }

    [Test]
    public void A_timeout_is_transient()
    {
        _dialect.IsTransient(new TimeoutException()).Should().BeTrue();
    }
}
