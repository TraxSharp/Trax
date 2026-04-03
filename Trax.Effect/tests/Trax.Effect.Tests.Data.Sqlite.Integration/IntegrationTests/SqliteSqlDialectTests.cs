using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Extensions;

namespace Trax.Effect.Tests.Data.Sqlite.Integration.IntegrationTests;

[TestFixture]
public class SqliteSqlDialectTests
{
    private ISqlDialect _dialect = null!;
    private ServiceProvider _provider = null!;
    private string _dbPath = null!;

    [OneTimeSetUp]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"trax_dialect_test_{Guid.NewGuid():N}.db");
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

    #region ClaimWorkQueueEntry

    [Test]
    public void ClaimWorkQueueEntry_DoesNotContainForUpdateSkipLocked()
    {
        var sql = _dialect.ClaimWorkQueueEntry();

        sql.Should().NotContainEquivalentOf("FOR UPDATE SKIP LOCKED");
    }

    [Test]
    public void ClaimWorkQueueEntry_DoesNotContainSchemaPrefix()
    {
        var sql = _dialect.ClaimWorkQueueEntry();

        sql.Should().NotContain("trax.");
    }

    [Test]
    public void ClaimWorkQueueEntry_ContainsStatusQueued()
    {
        var sql = _dialect.ClaimWorkQueueEntry();

        sql.Should().Contain("'queued'");
    }

    #endregion

    #region DequeueBackgroundJobs

    [Test]
    public void DequeueBackgroundJobs_UsesDatetimeFunction_NotNow()
    {
        var sql = _dialect.DequeueBackgroundJobs();

        sql.Should().Contain("datetime(");
        sql.Should().NotContain("NOW()");
    }

    [Test]
    public void DequeueBackgroundJobs_DoesNotContainMakeInterval()
    {
        var sql = _dialect.DequeueBackgroundJobs();

        sql.Should().NotContainEquivalentOf("make_interval");
    }

    [Test]
    public void DequeueBackgroundJobs_DoesNotContainForUpdateSkipLocked()
    {
        var sql = _dialect.DequeueBackgroundJobs();

        sql.Should().NotContainEquivalentOf("FOR UPDATE SKIP LOCKED");
    }

    [Test]
    public void DequeueBackgroundJobs_ContainsLimitPlaceholder()
    {
        var sql = _dialect.DequeueBackgroundJobs();

        sql.Should().Contain("LIMIT {1}");
    }

    #endregion

    #region LoadGroupFairQueuedJobs

    [Test]
    public void LoadGroupFairQueuedJobs_UsesDatetimeNow_NotPostgresNow()
    {
        var sql = _dialect.LoadGroupFairQueuedJobs();

        sql.Should().Contain("datetime('now')");
        sql.Should().NotContain("NOW()");
    }

    [Test]
    public void LoadGroupFairQueuedJobs_UsesBooleanInteger_NotTrue()
    {
        var sql = _dialect.LoadGroupFairQueuedJobs();

        sql.Should().Contain("= 1");
        sql.Should().NotContain("= true");
        sql.Should().NotContain("= TRUE");
    }

    [Test]
    public void LoadGroupFairQueuedJobs_DoesNotContainSchemaPrefix()
    {
        var sql = _dialect.LoadGroupFairQueuedJobs();

        sql.Should().NotContain("trax.");
    }

    [Test]
    public void LoadGroupFairQueuedJobs_ContainsRowNumber()
    {
        var sql = _dialect.LoadGroupFairQueuedJobs();

        sql.Should().Contain("ROW_NUMBER()");
    }

    [Test]
    public void LoadGroupFairQueuedJobs_ContainsPartitionBy()
    {
        var sql = _dialect.LoadGroupFairQueuedJobs();

        sql.Should().ContainEquivalentOf("PARTITION BY");
    }

    #endregion

    #region TryAcquireLeaderLock

    [Test]
    public void TryAcquireLeaderLock_AlwaysReturnsTrue()
    {
        var sql = _dialect.TryAcquireLeaderLock("test_lock").ToString();

        sql.Should().Contain("SELECT 1");
    }

    [Test]
    public void TryAcquireLeaderLock_DoesNotContainAdvisoryLock()
    {
        var sql = _dialect.TryAcquireLeaderLock("test_lock").ToString();

        sql.Should().NotContainEquivalentOf("pg_try_advisory_lock");
        sql.Should().NotContainEquivalentOf("advisory");
    }

    #endregion
}
