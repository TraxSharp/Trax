using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Extensions;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Extensions;

namespace Trax.Effect.Tests.Data.Sqlite.Integration.IntegrationTests;

[TestFixture]
public class SqliteSqlDialectTests
{
    private ISqlDialect? _dialect;
    private ServiceProvider? _provider;
    private string? _dbPath;

    [OneTimeSetUp]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"trax_dialect_test_{Guid.NewGuid():N}.db");
        _provider = new ServiceCollection()
            .AddTrax(trax =>
                trax.AddEffects(effects => effects.UseSqlite($"Data Source={_dbPath.NotNull()}"))
            )
            .BuildServiceProvider();
        _dialect = _provider.NotNull().GetRequiredService<ISqlDialect>();
    }

    [OneTimeTearDown]
    public async Task TearDown()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
        if (File.Exists(_dbPath.NotNull()))
            File.Delete(_dbPath.NotNull());
    }

    #region ClaimWorkQueueEntry

    [Test]
    public void ClaimWorkQueueEntry_DoesNotContainForUpdateSkipLocked()
    {
        var sql = _dialect.NotNull().ClaimWorkQueueEntry();

        sql.Should().NotContainEquivalentOf("FOR UPDATE SKIP LOCKED");
    }

    [Test]
    public void ClaimWorkQueueEntry_DoesNotContainSchemaPrefix()
    {
        var sql = _dialect.NotNull().ClaimWorkQueueEntry();

        sql.Should().NotContain("trax.");
    }

    [Test]
    public void ClaimWorkQueueEntry_ComparesStatusToTheStoredInteger()
    {
        var sql = _dialect.NotNull().ClaimWorkQueueEntry();

        // EF stores the enum's integer on SQLite, so a label here would match no row. Whether
        // the claim actually finds an entry is SqliteDispatchSqlTests' job.
        sql.Should().Contain($"w.status = {(int)Trax.Effect.Enums.WorkQueueStatus.Queued}");
        sql.Should().NotContain("'queued'");
    }

    #endregion

    #region DequeueBackgroundJobs

    [Test]
    public void DequeueBackgroundJobs_UsesDatetimeFunction_NotNow()
    {
        var sql = _dialect.NotNull().DequeueBackgroundJobs();

        sql.Should().Contain("datetime(");
        sql.Should().NotContain("NOW()");
    }

    [Test]
    public void DequeueBackgroundJobs_DoesNotContainMakeInterval()
    {
        var sql = _dialect.NotNull().DequeueBackgroundJobs();

        sql.Should().NotContainEquivalentOf("make_interval");
    }

    [Test]
    public void DequeueBackgroundJobs_DoesNotContainForUpdateSkipLocked()
    {
        var sql = _dialect.NotNull().DequeueBackgroundJobs();

        sql.Should().NotContainEquivalentOf("FOR UPDATE SKIP LOCKED");
    }

    [Test]
    public void DequeueBackgroundJobs_ContainsLimitPlaceholder()
    {
        var sql = _dialect.NotNull().DequeueBackgroundJobs();

        sql.Should().Contain("LIMIT {1}");
    }

    #endregion

    #region LoadGroupFairQueuedJobs

    [Test]
    public void LoadGroupFairQueuedJobs_UsesDatetimeNow_NotPostgresNow()
    {
        var sql = _dialect.NotNull().LoadGroupFairQueuedJobs();

        sql.Should().Contain("datetime('now')");
        sql.Should().NotContain("NOW()");
    }

    [Test]
    public void LoadGroupFairQueuedJobs_UsesBooleanInteger_NotTrue()
    {
        var sql = _dialect.NotNull().LoadGroupFairQueuedJobs();

        sql.Should().Contain("= 1");
        sql.Should().NotContain("= true");
        sql.Should().NotContain("= TRUE");
    }

    [Test]
    public void LoadGroupFairQueuedJobs_DoesNotContainSchemaPrefix()
    {
        var sql = _dialect.NotNull().LoadGroupFairQueuedJobs();

        sql.Should().NotContain("trax.");
    }

    [Test]
    public void LoadGroupFairQueuedJobs_ContainsRowNumber()
    {
        var sql = _dialect.NotNull().LoadGroupFairQueuedJobs();

        sql.Should().Contain("ROW_NUMBER()");
    }

    [Test]
    public void LoadGroupFairQueuedJobs_ContainsPartitionBy()
    {
        var sql = _dialect.NotNull().LoadGroupFairQueuedJobs();

        sql.Should().ContainEquivalentOf("PARTITION BY");
    }

    #endregion

    #region EstimateRowCount

    [Test]
    public void EstimateRowCount_IsNull_BecauseSqliteKeepsNoRowEstimate()
    {
        _dialect
            .NotNull()
            .EstimateRowCount()
            .Should()
            .BeNull(
                "SQLite keeps no planner row estimate without sqlite_stat1, and a caller should "
                    + "count instead"
            );
    }

    #endregion

    #region TryAcquireLeaderLock

    [Test]
    public void TryAcquireLeaderLock_AlwaysReturnsTrue()
    {
        var sql = _dialect
            .NotNull()
            .TryAcquireLeaderLock("test_lock")
            .ToString(System.Globalization.CultureInfo.InvariantCulture);

        sql.Should().Contain("SELECT 1");
    }

    [Test]
    public void TryAcquireLeaderLock_DoesNotContainAdvisoryLock()
    {
        var sql = _dialect
            .NotNull()
            .TryAcquireLeaderLock("test_lock")
            .ToString(System.Globalization.CultureInfo.InvariantCulture);

        sql.Should().NotContainEquivalentOf("pg_try_advisory_lock");
        sql.Should().NotContainEquivalentOf("advisory");
    }

    #endregion
}
