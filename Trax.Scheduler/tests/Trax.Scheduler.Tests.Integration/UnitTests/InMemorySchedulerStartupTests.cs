using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Trains.JobDispatcher;

namespace Trax.Scheduler.Tests.Integration.UnitTests;

/// <summary>
/// A scheduler on the InMemory provider dispatches from the manifest manager inline, so it has no
/// job dispatcher to run. It must start: the mediator's chain check refuses a host with any
/// registered train it could never build, and the dispatcher's junctions need a SQL dialect that
/// only a database provider registers.
/// </summary>
[TestFixture]
public class InMemorySchedulerStartupTests
{
    // AddMediator scans an assembly with no trains, so the host holds exactly what AddScheduler
    // registers, as an application that scans only its own assembly does.
    [Test]
    public async Task A_scheduler_on_the_in_memory_provider_starts()
    {
        using var host = new HostBuilder()
            .ConfigureServices(services =>
                services
                    .AddLogging()
                    .AddTrax(trax =>
                        trax.AddEffects(effects => effects.UseInMemory())
                            .AddMediator(typeof(HostBuilder).Assembly)
                            .AddScheduler()
                    )
            )
            .Build();

        await host.Invoking(h => h.StartAsync()).Should().NotThrowAsync();

        await host.StopAsync();
    }

    [Test]
    public void A_scheduler_on_the_in_memory_provider_registers_no_job_dispatcher()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddTrax(trax =>
                trax.AddEffects(effects => effects.UseInMemory())
                    .AddMediator(typeof(HostBuilder).Assembly)
                    .AddScheduler()
            );

        services.Should().NotContain(d => d.ServiceType == typeof(IJobDispatcherTrain));
    }
}
