using System.Reflection;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Core.Extensions;
using Trax.Core.Functional;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Sqlite.Extensions;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.JunctionRun;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.Utils;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.JobDispatcher;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// A train with a <c>Parallel</c> step, queued on a scheduler and run on a separate worker host
/// that it reaches only over HTTP (<c>UseTraxJobRunner</c> behind a test server), on Postgres and
/// SQLite: the run completes, and the worker records each branch's junctions under their branch.
/// </summary>
[TestFixture(ClusterStore.Postgres)]
[TestFixture(ClusterStore.Sqlite)]
[NonParallelizable]
public class RemoteParallelRunTests(ClusterStore store)
{
    private const string WorkerUrl = "http://worker.test/trax/execute";

    private InvokeCluster? _cluster;
    private IHost? _worker;
    private ClusterHost? _scheduler;

    [OneTimeSetUp]
    public async Task CreateHosts()
    {
        _cluster = await InvokeCluster.Create(store);

        _worker = await new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddLogging(x => x.AddConsole().SetMinimumLevel(LogLevel.Warning));
                        services.AddRouting();
                        services.AddTrax(trax =>
                            trax.AddEffects(effects =>
                                    (
                                        store == ClusterStore.Postgres
                                            ? effects.UsePostgres(
                                                _cluster.NotNull().ConnectionString
                                            )
                                            : effects.UseSqlite(_cluster.NotNull().ConnectionString)
                                    )
                                        .SaveTrainParameters()
                                        .AddJunctionEvents()
                                        // Some trains in this assembly ask a decider, and a runner
                                        // that does not record decisions refuses to start.
                                        .AddDecisionRecording()
                                )
                                // This assembly holds trains built to fail chain verification,
                                // which a started host would refuse (the Parallel train is not
                                // one), and the worker serves no API submissions.
                                .AddMediator(mediator =>
                                    mediator
                                        .ScanAssemblies(typeof(AssemblyMarker).Assembly)
                                        .SkipChainVerification()
                                        .AllowMissingAuthorizationService()
                                )
                        );
                        services.AddTraxJobRunner(runner => runner.AllowUnsignedRequests());
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseEndpoints(endpoints => endpoints.UseTraxJobRunner());
                    })
            )
            .StartAsync();

        var toWorker = _worker.NotNull().GetTestServer().CreateHandler();
        _scheduler = _cluster
            .NotNull()
            .Host(
                machines: false,
                scheduler: true,
                scheduling: s =>
                    s.OverrideSubmitter(x =>
                        x.AddScoped<IJobSubmitter>(_ => new HttpJobSubmitter(
                            new HttpClient(toWorker, disposeHandler: false)
                            {
                                BaseAddress = new Uri(WorkerUrl),
                                Timeout = TimeSpan.FromSeconds(30),
                            },
                            new RemoteWorkerOptions
                            {
                                BaseUrl = WorkerUrl,
                                Retry = new HttpRetryOptions { MaxRetries = 0 },
                            },
                            NullLogger<HttpJobSubmitter>.Instance,
                            TimeProvider.System
                        ))
                    ),
                // No junction events here: every junction_run row the test reads is the worker's.
                data: d => d.SaveTrainParameters()
            );
    }

    [OneTimeTearDown]
    public async Task DisposeHosts()
    {
        if (_worker is not null)
        {
            await _worker.NotNull().StopAsync();
            _worker?.Dispose();
        }
        if (_scheduler is not null)
            if (_scheduler is not null)
                await _scheduler.DisposeAsync();
        if (_cluster is not null)
            await _cluster.DisposeAsync();
    }

    [SetUp]
    public Task Reset() => _cluster.NotNull().Reset();

    [Test]
    public async Task A_parallel_train_runs_end_to_end_on_a_remote_worker()
    {
        var (manifest, entry) = await Queue(new ParallelRemoteInput { Topic = "rail" });

        var scope = _scheduler.NotNull().Services.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
            await scope
                .ServiceProvider.GetRequiredService<IJobDispatcherTrain>()
                .Run(Unit.Default)
                .WaitAsync(TimeSpan.FromSeconds(60), TimeProvider.System);

        var dispatched = await With(data =>
            data.WorkQueues.AsNoTracking().SingleAsync(q => q.Id == entry.Id)
        );
        dispatched.Status.Should().Be(WorkQueueStatus.Dispatched);
        dispatched.DispatchAttempts.Should().Be(0, "the worker took the job on the first delivery");

        var run = await With(data =>
            data.Metadatas.AsNoTracking().SingleAsync(m => m.ManifestId == manifest.Id)
        );
        run.Id.Should().Be(dispatched.MetadataId);
        run.TrainState.Should().Be(TrainState.Completed, run.FailureReason);
        run.Output.Should().Contain("left=8, right=40", "the join read both branches' results");

        var rows = await JunctionRuns(run.Id);
        rows.Should()
            .OnlyContain(r => r.Kind == JunctionRunKind.Junction)
            .And.OnlyContain(r => r.State == JunctionRunState.Completed);
        rows.Select(r => (r.Name, r.NodeId, r.BranchPath))
            .Should()
            .BeEquivalentTo([
                (nameof(ParallelRemoteAdmit), "ParallelRemoteAdmit#0", null),
                (
                    nameof(ParallelRemoteScoreLeft),
                    "Parallel#0/left/ParallelRemoteScoreLeft#0",
                    "Parallel#0/left"
                ),
                (
                    nameof(ParallelRemoteRefineLeft),
                    "Parallel#0/left/ParallelRemoteRefineLeft#0",
                    "Parallel#0/left"
                ),
                (
                    nameof(ParallelRemoteScoreRight),
                    "Parallel#0/right/ParallelRemoteScoreRight#0",
                    "Parallel#0/right"
                ),
                (nameof(ParallelRemoteCombine), "ParallelRemoteCombine#0", (string?)null),
            ]);

        var left = rows.Where(r =>
                string.Equals(r.BranchPath, "Parallel#0/left", StringComparison.Ordinal)
            )
            .ToList();
        left.Select(r => r.Name)
            .Should()
            .Equal(
                [nameof(ParallelRemoteScoreLeft), nameof(ParallelRemoteRefineLeft)],
                "a branch's own junctions are recorded in the order they ran"
            );
        rows[0].Name.Should().Be(nameof(ParallelRemoteAdmit));
        rows[^1].Name.Should().Be(nameof(ParallelRemoteCombine));
    }

    private async Task<(Manifest, WorkQueue)> Queue(ParallelRemoteInput input)
    {
        var scope = _scheduler.NotNull().Services.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var data = scope.ServiceProvider.GetRequiredService<IDataContext>();
        var group = await TestSetup
            .CreateAndSaveManifestGroup(data, name: $"group-{Guid.NewGuid():N}")
            .ConfigureAwait(false);

        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(ParallelRemoteTrain),
                IsEnabled = true,
                ScheduleType = ScheduleType.None,
                MaxRetries = 0,
                Properties = input,
            }
        );
        manifest.ManifestGroupId = group.Id;
        await data.Track(manifest).ConfigureAwait(false);
        await data.SaveChanges(CancellationToken.None).ConfigureAwait(false);

        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = typeof(IParallelRemoteTrain).FullName.NotNull(),
                Input = JsonSerializer.Serialize(
                    input,
                    TraxJsonSerializationOptions.ManifestProperties
                ),
                InputTypeName = typeof(ParallelRemoteInput).FullName,
                ManifestId = manifest.Id,
            },
            TimeProvider.System
        );
        await data.Track(entry).ConfigureAwait(false);
        await data.SaveChanges(CancellationToken.None).ConfigureAwait(false);
        return (manifest, entry);
    }

    /// <summary>
    /// The run's junction events, once the worker's writer has written everything queued before
    /// the call: it writes in the background, after the run has answered.
    /// </summary>
    private async Task<List<JunctionRun>> JunctionRuns(long runId)
    {
        // The writer's flush is internal to Trax.Effect.Data, which this assembly cannot see.
        var writer = _worker
            .NotNull()
            .Services.GetRequiredService(
                typeof(Effect.Data.Extensions.ServiceExtensions)
                    .Assembly.GetType(
                        "Trax.Effect.Data.JunctionEvents.JunctionRunWriter",
                        throwOnError: true
                    )
                    .NotNull()
            );
        await (
            (Task)
                writer
                    .GetType()
                    .GetMethod("FlushAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                    .NotNull()
                    .Invoke(writer, [CancellationToken.None])
                    .NotNull()
        )
            .WaitAsync(TimeSpan.FromSeconds(30), TimeProvider.System)
            .ConfigureAwait(false);

        return await With(data => data.JunctionRuns.AsNoTracking().ForRun(runId).ToListAsync())
            .ConfigureAwait(false);
    }

    private async Task<T> With<T>(Func<IDataContext, Task<T>> read)
    {
        var scope = _scheduler.NotNull().Services.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        return await read(scope.ServiceProvider.GetRequiredService<IDataContext>())
            .ConfigureAwait(false);
    }
}
