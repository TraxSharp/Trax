using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// A scheduler host that also serves as a runner (it maps a runner endpoint for another scheduler,
/// or adds a worker pool) keeps the scheduler it configured.
/// </summary>
[TestFixture]
public class RunnerRegistrationBesideSchedulerTests
{
    [Test]
    public async Task AddTraxJobRunner_in_a_scheduler_host_keeps_the_schedulers_configuration()
    {
        await using var provider = new ServiceCollection()
            .AddLogging(x => x.SetMinimumLevel(LogLevel.Warning))
            .AddTrax(trax =>
                trax.AddEffects(effects => effects.UsePostgres(TestPostgres.ConnectionString))
                    .AddMediator(typeof(AssemblyMarker).Assembly, typeof(JobRunnerTrain).Assembly)
                    .AddScheduler(scheduler => scheduler.MaxActiveJobs(3))
            )
            .AddTraxJobRunner(runner => runner.AllowUnsignedRequests())
            .BuildServiceProvider();

        var configuration = provider.GetRequiredService<SchedulerConfiguration>();

        configuration
            .MaxActiveJobs.Should()
            .Be(3, "the scheduler was configured with MaxActiveJobs(3)");
        configuration
            .HasDatabaseProvider.Should()
            .BeTrue("the host uses Postgres, so the ManifestManager must take its leader lock");
    }

    [Test]
    public async Task AddTraxJobRunner_before_the_scheduler_still_leaves_the_schedulers_configuration()
    {
        await using var provider = new ServiceCollection()
            .AddLogging(x => x.SetMinimumLevel(LogLevel.Warning))
            .AddTraxJobRunner(runner => runner.AllowUnsignedRequests())
            .AddTrax(trax =>
                trax.AddEffects(effects => effects.UsePostgres(TestPostgres.ConnectionString))
                    .AddMediator(typeof(AssemblyMarker).Assembly, typeof(JobRunnerTrain).Assembly)
                    .AddScheduler(scheduler => scheduler.MaxActiveJobs(3))
            )
            .BuildServiceProvider();

        provider.GetRequiredService<SchedulerConfiguration>().MaxActiveJobs.Should().Be(3);
        provider.GetServices<SchedulerConfiguration>().Should().ContainSingle();
    }

    [Test]
    public void AddTraxWorker_in_a_scheduler_host_that_runs_local_workers_is_refused()
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddTrax(trax =>
                trax.AddEffects(effects => effects.UsePostgres(TestPostgres.ConnectionString))
                    .AddMediator(typeof(AssemblyMarker).Assembly, typeof(JobRunnerTrain).Assembly)
                    .AddScheduler(scheduler => scheduler.MaxActiveJobs(3))
            );

        var act = () => services.AddTraxWorker(o => o.WorkerCount = 2);

        act.Should().Throw<InvalidOperationException>().WithMessage("*ConfigureLocalWorkers*");
    }

    [Test]
    public void AddTraxWorker_before_a_scheduler_that_runs_local_workers_is_refused()
    {
        var services = new ServiceCollection().AddLogging().AddTraxWorker(o => o.WorkerCount = 2);

        var act = () =>
            services.AddTrax(trax =>
                trax.AddEffects(effects => effects.UsePostgres(TestPostgres.ConnectionString))
                    .AddMediator(typeof(AssemblyMarker).Assembly, typeof(JobRunnerTrain).Assembly)
                    .AddScheduler(scheduler => scheduler.MaxActiveJobs(3))
            );

        act.Should().Throw<InvalidOperationException>().WithMessage("*ConfigureLocalWorkers*");
    }
}
