using AwesomeAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.SchedulerStartupService;
using Trax.Scheduler.Tests.Sqlite.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Sqlite.Integration.IntegrationTests;

/// <summary>
/// Startup seeding retries a failure only when the Sqlite dialect calls it transient.
/// </summary>
[TestFixture]
public class SqliteSeedRetryClassificationTests : TestSetup
{
    private const int SqliteBusy = 5;
    private const int SqliteLocked = 6;
    private const int SqliteConstraint = 19;

    private SchedulerStartupService CreateService() =>
        new(
            Scope.ServiceProvider,
            new SchedulerConfiguration { RecoverStuckJobsOnStartup = false },
            NullLogger<SchedulerStartupService>.Instance
        );

    [TestCase(SqliteBusy)]
    [TestCase(SqliteLocked)]
    public void A_busy_or_locked_database_is_transient(int errorCode)
    {
        var failure = new DbUpdateException(
            "An error occurred while saving the entity changes.",
            new SqliteException("database is locked", errorCode)
        );

        CreateService().IsTransient(failure).Should().BeTrue();
    }

    [Test]
    public void A_constraint_violation_is_not_transient()
    {
        var failure = new DbUpdateException(
            "An error occurred while saving the entity changes.",
            new SqliteException("UNIQUE constraint failed", SqliteConstraint)
        );

        CreateService().IsTransient(failure).Should().BeFalse();
    }
}
