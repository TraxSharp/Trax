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

    /// <summary>
    /// The subject test is bounded by the runs in flight, not by the subject's history.
    /// </summary>
    /// <remarks>
    /// Both the correlated and the set form give the same answers, so every behavioural test
    /// passes either way and nothing else would notice a change back. What differs is the cost:
    /// correlated, the subquery re-walked one subject's dispatched rows, which accumulate for the
    /// life of the subject because a dispatched row never reaches a terminal status of its own, and
    /// it did so inside the transaction holding that subject's advisory lock. This pins the shape
    /// the measurement was taken against.
    /// </remarks>
    [Test]
    public void ClaimWorkQueueEntry_TestsTheSubjectAgainstAnUncorrelatedSet()
    {
        var dialect = Create();
        var sql = (string)
            dialect.GetType().GetMethod("ClaimWorkQueueEntry")!.Invoke(dialect, null)!;

        sql.Should()
            .Contain(
                "NOT IN (",
                "the busy subjects are gathered once, so the cost follows the work in flight"
            );
        sql.Should()
            .NotContain(
                "b.subject_key = w.subject_key",
                "correlating the subquery to the outer row is what made the claim walk the "
                    + "subject's whole dispatched history"
            );
        sql.Should()
            .Contain(
                "b.subject_key IS NOT NULL",
                "x NOT IN (a, NULL) is never true, so without this one dispatched run carrying no "
                    + "subject would refuse every keyed claim in the system"
            );
    }

    [Test]
    public void LoadGroupFairQueuedJobs_TestsTheSubjectAgainstAnUncorrelatedSet()
    {
        var dialect = Create();
        var sql = (string)
            dialect.GetType().GetMethod("LoadGroupFairQueuedJobs")!.Invoke(dialect, null)!;

        sql.Should().NotContain("b.subject_key = wq.subject_key");
        sql.Should().Contain("b.subject_key IS NOT NULL");
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
