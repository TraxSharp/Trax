using System.Reflection;
using FluentAssertions;
using NUnit.Framework;

namespace Trax.Effect.Tests.Integration.UnitTests.Services;

[TestFixture]
public class PostgresSqlDialectTests
{
    private static readonly Assembly PostgresAssembly = Assembly.Load("Trax.Effect.Data.Postgres");

    private static object Create() =>
        Activator.CreateInstance(
            PostgresAssembly.GetType(
                "Trax.Effect.Data.Postgres.Services.SqlDialect.PostgresSqlDialect",
                throwOnError: true
            )!,
            nonPublic: true
        )!;

    [Test]
    public void TryAcquireLeaderLock_BuildsAdvisoryLockSql()
    {
        var dialect = Create();
        var method = dialect.GetType().GetMethod("TryAcquireLeaderLock")!;
        var sql = method.Invoke(dialect, ["my-lock"])!;

        sql.ToString().Should().Contain("pg_try_advisory_xact_lock");
        sql.ToString().Should().Contain("my-lock");
    }

    [Test]
    public void ClaimWorkQueueEntry_BuildsForUpdateSkipLockedSql()
    {
        var dialect = Create();
        var method = dialect.GetType().GetMethod("ClaimWorkQueueEntry")!;
        var sql = (string)method.Invoke(dialect, null)!;

        sql.Should().Contain("FOR UPDATE SKIP LOCKED");
        sql.Should().Contain("trax.work_queue");
    }

    [Test]
    public void DequeueBackgroundJobs_BuildsLimitOrderedSql()
    {
        var dialect = Create();
        var method = dialect.GetType().GetMethod("DequeueBackgroundJobs")!;
        var sql = (string)method.Invoke(dialect, null)!;

        sql.Should().Contain("trax.background_job");
        sql.Should().Contain("ORDER BY priority DESC");
        sql.Should().Contain("FOR UPDATE SKIP LOCKED");
    }

    [Test]
    public void LoadGroupFairQueuedJobs_BuildsRowNumberPartitionSql()
    {
        var dialect = Create();
        var method = dialect.GetType().GetMethod("LoadGroupFairQueuedJobs")!;
        var sql = (string)method.Invoke(dialect, null)!;

        sql.Should().Contain("ROW_NUMBER()");
        sql.Should().Contain("PARTITION BY m.manifest_group_id");
        sql.Should().Contain("trax.manifest_group");
    }
}
