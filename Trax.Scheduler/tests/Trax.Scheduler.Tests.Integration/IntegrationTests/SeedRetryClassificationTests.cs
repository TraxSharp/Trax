using System.Net.Sockets;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.SchedulerStartupService;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Startup seeding retries a failure only when the Postgres dialect calls it transient.
/// </summary>
[TestFixture]
public class SeedRetryClassificationTests : TestSetup
{
    private SchedulerStartupService CreateService() =>
        new(
            Scope.ServiceProvider,
            new SchedulerConfiguration { RecoverStuckJobsOnStartup = false },
            NullLogger<SchedulerStartupService>.Instance
        );

    [Test]
    public void A_lost_connection_is_transient()
    {
        var failure = new InvalidOperationException(
            "An exception has been raised that is likely due to a transient failure.",
            new NpgsqlException("Exception while connecting", new SocketException())
        );

        CreateService().IsTransient(failure).Should().BeTrue();
    }

    [Test]
    public void A_deadlock_is_transient()
    {
        var deadlock = new PostgresException("deadlock detected", "ERROR", "ERROR", "40P01");

        CreateService().IsTransient(deadlock).Should().BeTrue();
    }

    [Test]
    public void A_unique_violation_is_not_transient()
    {
        var conflict = new PostgresException("duplicate key", "ERROR", "ERROR", "23505");

        CreateService().IsTransient(conflict).Should().BeFalse();
    }
}
