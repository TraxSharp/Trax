using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Core.Extensions;
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

    private InvokeCluster? _cluster;

    // Dispatches, runs and registers the machines, so the run's failure is delivered by its hook.
    private ClusterHost? _full;

    [OneTimeSetUp]
    public async Task CreateCluster()
    {
        _cluster = await InvokeCluster.Create(store);
        _full = _cluster.NotNull().Host(machines: true, scheduler: true);
    }

    [OneTimeTearDown]
    public async Task DisposeCluster()
    {
        if (_full is not null)
            await _full.DisposeAsync();
        if (_cluster is not null)
            await _cluster.DisposeAsync();
    }

    [SetUp]
    public async Task Reset()
    {
        InvokedStepGate.Reset();
        await _cluster.NotNull().Reset();
    }

    [Test]
    public async Task Job_failing_before_run_fails_the_run()
    {
        var instance = await _full.NotNull().Start(StepMachine.Context(InvokedStepModes.Ok));
        var token = (await _full.NotNull().Row(instance.Id, SystemStepMachine.MachineId))
            .NotNull()
            .InvokeToken.NotNull();
        var runId = await _full.NotNull().Dispatch(token);

        // A job the worker cannot even start: its input type is no registered train's input.
        long jobId;
        var scope = _full.NotNull().Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
        {
            var context = scope.ServiceProvider.GetRequiredService<IDataContext>();
            var job = BackgroundJob.Create(
                new CreateBackgroundJob
                {
                    MetadataId = runId,
                    Input = "{}",
                    InputType = "Nowhere.NotAnInput",
                },
                TimeProvider.System
            );
            await context.Track(job);
            await context.SaveChanges(CancellationToken.None);
            jobId = job.Id;
        }

        var worker = new LocalWorkerService(
            _full.NotNull().Services,
            new LocalWorkerOptions
            {
                WorkerCount = 1,
                PollingInterval = TimeSpan.FromMilliseconds(100),
            },
            new CancellationRegistry(),
            NullLogger<LocalWorkerService>.Instance,
            _full.NotNull().Services.GetRequiredService<ISqlDialect>()
        );
        await worker.StartAsync(CancellationToken.None);
        try
        {
            await JobGone(jobId).WaitAsync(Bound, TimeProvider.System);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }

        (await _full.NotNull().Run(runId))
            .TrainState.Should()
            .Be(TrainState.Failed, "nothing will ever start the run once its job is gone");
        (await _full.NotNull().Row(instance.Id, SystemStepMachine.MachineId))
            .NotNull()
            .State.Should()
            .Be("Failed", "the run's failure reaches the machine's OnFailed at once");
    }

    private async Task JobGone(long jobId)
    {
        while (true)
        {
            var scope = _full.NotNull().Services.CreateAsyncScope();
            await using (scope.ConfigureAwait(false))
            {
                var context = scope.ServiceProvider.GetRequiredService<IDataContext>();
                if (
                    !await context
                        .BackgroundJobs.AsNoTracking()
                        .AnyAsync(j => j.Id == jobId)
                        .ConfigureAwait(false)
                )
                    return;
            }
            await Task.Delay(50).ConfigureAwait(false); // determinism: polling interval, bounded by the caller's WaitAsync
        }
    }
}
