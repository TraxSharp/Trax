using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Enums;
using Trax.Effect.Models.BackgroundJob;
using Trax.Effect.Models.BackgroundJob.DTOs;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Services.CancellationRegistry;
using Trax.Scheduler.Services.LocalWorkerService;
using Trax.Scheduler.Tests.Integration.Fakes.InvokedTrains;
using Trax.Scheduler.Tests.Integration.Fixtures;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests.InvokedTrains;

/// <summary>
/// A local worker whose job fails before its run starts fails the run itself, so the machine that
/// invoked it hears at once instead of waiting for the stale-pending reaper.
/// </summary>
[TestFixture(ClusterStore.Postgres)]
[TestFixture(ClusterStore.Sqlite)]
[NonParallelizable]
public class LocalWorkerTests(ClusterStore store)
{
    private static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);

    private InvokeCluster _cluster = null!;

    // Dispatches, runs and registers the machines, so the run's failure is delivered by its hook.
    private ClusterHost _full = null!;

    [OneTimeSetUp]
    public async Task CreateCluster()
    {
        _cluster = await InvokeCluster.Create(store);
        _full = _cluster.Host(machines: true, scheduler: true);
    }

    [OneTimeTearDown]
    public async Task DisposeCluster()
    {
        await _full.DisposeAsync();
        await _cluster.DisposeAsync();
    }

    [SetUp]
    public async Task Reset()
    {
        InvokedStepGate.Reset();
        await _cluster.Reset();
    }

    [Test]
    public async Task Job_failing_before_run_fails_the_run()
    {
        var instance = await _full.Start(StepMachine.Context(InvokedStepModes.Ok));
        var token = (await _full.Row(instance.Id, SystemStepMachine.MachineId))!.InvokeToken!;
        var runId = await _full.Dispatch(token);

        // A job the worker cannot even start: its input type is no registered train's input.
        long jobId;
        using (var scope = _full.Services.CreateScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<IDataContext>();
            var job = BackgroundJob.Create(
                new CreateBackgroundJob
                {
                    MetadataId = runId,
                    Input = "{}",
                    InputType = "Nowhere.NotAnInput",
                }
            );
            await context.Track(job);
            await context.SaveChanges(CancellationToken.None);
            jobId = job.Id;
        }

        var worker = new LocalWorkerService(
            _full.Services,
            new LocalWorkerOptions
            {
                WorkerCount = 1,
                PollingInterval = TimeSpan.FromMilliseconds(100),
            },
            new CancellationRegistry(),
            NullLogger<LocalWorkerService>.Instance,
            _full.Services.GetRequiredService<ISqlDialect>()
        );
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await JobGone(jobId).WaitAsync(Bound);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        (await _full.Run(runId))
            .TrainState.Should()
            .Be(TrainState.Failed, "nothing will ever start the run once its job is gone");
        (await _full.Row(instance.Id, SystemStepMachine.MachineId))!
            .State.Should()
            .Be("Failed", "the run's failure reaches the machine's OnFailed at once");
    }

    private async Task JobGone(long jobId)
    {
        while (true)
        {
            using (var scope = _full.Services.CreateScope())
            {
                var context = scope.ServiceProvider.GetRequiredService<IDataContext>();
                if (!await context.BackgroundJobs.AsNoTracking().AnyAsync(j => j.Id == jobId))
                    return;
            }
            await Task.Delay(50); // determinism: polling interval, bounded by the caller's WaitAsync
        }
    }
}
