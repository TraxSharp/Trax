using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Samples.EnergyHub.E2E.Fixtures;
using Trax.Scheduler.Services.JobSubmitter;

namespace Trax.Samples.EnergyHub.E2E.HubTests;

/// <summary>
/// The hub schedules and queues, and leaves execution to the Worker process: it registers
/// <c>PostgresJobSubmitter</c> through <c>OverrideSubmitter</c>, so no local worker starts, and
/// the worker is where <c>LocalWorkerService</c> runs.
/// </summary>
[TestFixture]
public class HubExecutesNoTrainsTests : HubTestFixture
{
    [Test]
    public void The_hub_runs_no_local_worker()
    {
        HostedServiceNames(SharedHubSetup.Factory.Services)
            .Should()
            .NotContain(
                "LocalWorkerService",
                "the hub leaves execution to the Worker process, as its Program.cs states"
            );
    }

    [Test]
    public void The_hub_writes_jobs_to_the_background_job_table()
    {
        Scope
            .ServiceProvider.GetRequiredService<IJobSubmitter>()
            .Should()
            .BeOfType<PostgresJobSubmitter>();
    }

    [Test]
    public void The_worker_runs_the_local_worker()
    {
        HostedServiceNames(SharedHubSetup.Worker.Services).Should().Contain("LocalWorkerService");
    }

    private static List<string> HostedServiceNames(IServiceProvider services) =>
        services.GetServices<IHostedService>().Select(s => s.GetType().Name).ToList();
}
