using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.Utils;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.JobDispatcher;
using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// With <c>UseRemoteWorkers</c>, a dispatch is a delivery, not an execution. Only a failure to
/// deliver the job, before the runner started it, is retried by the dispatcher. A runner that ran
/// the job and reported its failure, or that is still running it when the HTTP call times out, has
/// the run: its outcome is recorded on the run's row, and the dispatcher does not run it again.
/// </summary>
[TestFixture]
public class RemoteDeliveryOutcomeTests
{
    private ServiceProvider _serviceProvider = null!;
    private IServiceScope _scope = null!;
    private IDataContext _dataContext = null!;
    private static readonly StubRunner Runner = new();

    [OneTimeSetUp]
    public void RunBeforeAnyTests()
    {
        _serviceProvider = new ServiceCollection()
            .AddLogging(x => x.AddConsole().SetMinimumLevel(LogLevel.Information))
            .AddTrax(trax =>
                trax.AddEffects(effects =>
                        effects
                            .SaveTrainParameters()
                            .UsePostgres(TestPostgres.ConnectionString)
                            .AddJson()
                    )
                    .AddMediator(typeof(AssemblyMarker).Assembly, typeof(JobRunnerTrain).Assembly)
                    .AddScheduler(scheduler =>
                        scheduler.OverrideSubmitter(s =>
                            s.AddScoped<IJobSubmitter>(_ => new HttpJobSubmitter(
                                new HttpClient(Runner)
                                {
                                    BaseAddress = new Uri("https://runner.test/trax/execute"),
                                    Timeout = TimeSpan.FromMilliseconds(500),
                                },
                                new RemoteWorkerOptions
                                {
                                    BaseUrl = "https://runner.test/trax/execute",
                                    Retry = new HttpRetryOptions { MaxRetries = 0 },
                                },
                                NullLogger<HttpJobSubmitter>.Instance
                            ))
                        )
                    )
            )
            .AddScoped<IDataContext>(sp =>
                (IDataContext)sp.GetRequiredService<IDataContextProviderFactory>().Create()
            )
            .BuildServiceProvider();

        Runner.Services = _serviceProvider;
    }

    [OneTimeTearDown]
    public async Task RunAfterAnyTests() => await _serviceProvider.DisposeAsync();

    [SetUp]
    public async Task TestSetUp()
    {
        _scope = _serviceProvider.CreateScope();
        _dataContext = _scope.ServiceProvider.GetRequiredService<IDataContext>();
        await TestSetup.CleanupDatabase(_dataContext);
    }

    [TearDown]
    public void TestTearDown()
    {
        if (_dataContext is IDisposable disposable)
            disposable.Dispose();
        _scope.Dispose();
        Runner.Mode = StubRunnerMode.RunAndReport;
    }

    [Test]
    public async Task A_train_that_fails_on_the_runner_runs_once_over_five_dispatch_cycles()
    {
        var key = Guid.NewGuid().ToString("N");
        var (manifest, entry) = await Queue(new DeliveryProbeInput { Key = key, Fail = true });
        Runner.Mode = StubRunnerMode.RunAndReport;

        try
        {
            await RunDispatchCycles(5);

            DeliveryProbeTrain
                .Runs[key]
                .Should()
                .Be(1, "the runner ran it and reported its failure");

            _dataContext.Reset();
            var runs = await _dataContext
                .Metadatas.AsNoTracking()
                .Where(m => m.ManifestId == manifest.Id)
                .ToListAsync();
            runs.Should().ContainSingle("the failure is the run's outcome, not a failed delivery");
            runs[0].TrainState.Should().Be(TrainState.Failed);
            runs[0].FailureReason.Should().Contain("failed as asked", "the runner's record stands");

            var queued = await _dataContext
                .WorkQueues.AsNoTracking()
                .SingleAsync(q => q.Id == entry.Id);
            queued.Status.Should().Be(WorkQueueStatus.Dispatched);
            queued.DispatchAttempts.Should().Be(0, "the job was delivered on the first attempt");
        }
        finally
        {
            DeliveryProbeTrain.Forget(key);
        }
    }

    [Test]
    public async Task A_train_still_running_when_the_http_call_times_out_runs_once()
    {
        var key = Guid.NewGuid().ToString("N");
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        DeliveryProbeTrain.Gates[key] = gate;
        var (manifest, entry) = await Queue(new DeliveryProbeInput { Key = key });
        Runner.Mode = StubRunnerMode.StartAndHang;

        try
        {
            await RunDispatchCycles(5);

            gate.SetResult();
            await Runner.Drain();

            DeliveryProbeTrain.Runs[key].Should().Be(1, "the first delivery was accepted and ran");

            _dataContext.Reset();
            var runs = await _dataContext
                .Metadatas.AsNoTracking()
                .Where(m => m.ManifestId == manifest.Id)
                .ToListAsync();
            runs.Should().ContainSingle();
            runs[0].TrainState.Should().Be(TrainState.Completed);

            var queued = await _dataContext
                .WorkQueues.AsNoTracking()
                .SingleAsync(q => q.Id == entry.Id);
            queued.Status.Should().Be(WorkQueueStatus.Dispatched);
        }
        finally
        {
            gate.TrySetResult();
            DeliveryProbeTrain.Forget(key);
        }
    }

    [Test]
    public async Task A_runner_that_refuses_the_job_before_starting_it_is_retried()
    {
        var key = Guid.NewGuid().ToString("N");
        var (manifest, entry) = await Queue(new DeliveryProbeInput { Key = key });
        Runner.Mode = StubRunnerMode.RefuseBeforeStart;

        try
        {
            await RunDispatchCycles(1);

            DeliveryProbeTrain.Runs.ContainsKey(key).Should().BeFalse();

            _dataContext.Reset();
            var queued = await _dataContext
                .WorkQueues.AsNoTracking()
                .SingleAsync(q => q.Id == entry.Id);
            queued.Status.Should().Be(WorkQueueStatus.Queued, "the job never reached the train");
            queued.DispatchAttempts.Should().Be(1);

            var run = await _dataContext
                .Metadatas.AsNoTracking()
                .SingleAsync(m => m.ManifestId == manifest.Id);
            run.TrainState.Should().Be(TrainState.Failed, "the attempt is recorded");
        }
        finally
        {
            DeliveryProbeTrain.Forget(key);
        }
    }

    private async Task RunDispatchCycles(int cycles)
    {
        for (var i = 0; i < cycles; i++)
        {
            using var cycleScope = _serviceProvider.CreateScope();
            var train = cycleScope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>();
            await train.Run(Unit.Default);

            // Stand in for time passing: a requeued entry waits out its dispatch backoff.
            _dataContext.Reset();
            await _dataContext
                .WorkQueues.Where(q => q.ScheduledAt != null)
                .ExecuteUpdateAsync(s => s.SetProperty(q => q.ScheduledAt, (DateTime?)null));
        }
    }

    private async Task<(Manifest, WorkQueue)> Queue(DeliveryProbeInput input)
    {
        var group = await TestSetup.CreateAndSaveManifestGroup(
            _dataContext,
            name: $"group-{Guid.NewGuid():N}"
        );

        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(DeliveryProbeTrain),
                IsEnabled = true,
                ScheduleType = ScheduleType.None,
                MaxRetries = 3,
                Properties = input,
            }
        );
        manifest.ManifestGroupId = group.Id;
        await _dataContext.Track(manifest);
        await _dataContext.SaveChanges(CancellationToken.None);

        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = typeof(IDeliveryProbeTrain).FullName!,
                Input = JsonSerializer.Serialize(
                    input,
                    TraxJsonSerializationOptions.ManifestProperties
                ),
                InputTypeName = typeof(DeliveryProbeInput).FullName,
                ManifestId = manifest.Id,
            }
        );
        await _dataContext.Track(entry);
        await _dataContext.SaveChanges(CancellationToken.None);
        _dataContext.Reset();

        return (manifest, entry);
    }

    private enum StubRunnerMode
    {
        /// <summary>Runs the job to the end and answers with its outcome, as a runner does.</summary>
        RunAndReport,

        /// <summary>Starts the job, then never answers, so the scheduler's HTTP call times out.</summary>
        StartAndHang,

        /// <summary>Answers with an error without starting the job.</summary>
        RefuseBeforeStart,
    }

    /// <summary>
    /// Stands in for a remote runner behind <c>/trax/execute</c>, running the JobRunner in this
    /// process against the same database.
    /// </summary>
    private sealed class StubRunner : HttpMessageHandler
    {
        private readonly List<Task> _running = [];

        public IServiceProvider Services { get; set; } = null!;

        public StubRunnerMode Mode { get; set; } = StubRunnerMode.RunAndReport;

        public async Task Drain()
        {
            Task[] running;
            lock (_running)
                running = [.. _running];
            await Task.WhenAll(running).WaitAsync(TimeSpan.FromSeconds(30));
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            var job = JsonSerializer.Deserialize<RemoteJobRequest>(
                await request.Content!.ReadAsStringAsync(cancellationToken),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
            )!;
            var input = JsonSerializer.Deserialize<DeliveryProbeInput>(
                job.Input!,
                TraxJsonSerializationOptions.ManifestProperties
            )!;

            switch (Mode)
            {
                case StubRunnerMode.RefuseBeforeStart:
                    return Answer(
                        new RemoteJobResponse(job.MetadataId, IsError: true, ErrorMessage: "no")
                    );

                case StubRunnerMode.StartAndHang:
                {
                    var started = DeliveryProbeTrain.Started.GetOrAdd(
                        input.Key,
                        _ => new TaskCompletionSource(
                            TaskCreationOptions.RunContinuationsAsynchronously
                        )
                    );
                    var run = RunJob(job.MetadataId, input);
                    lock (_running)
                        _running.Add(run.ContinueWith(_ => { }, TaskScheduler.Default));

                    // Answer nothing until the scheduler gives up, once the train is running.
                    await Task.WhenAny(started.Task, run);
                    await new TaskCompletionSource().Task.WaitAsync(cancellationToken);
                    throw new InvalidOperationException("unreachable");
                }

                default:
                    try
                    {
                        await RunJob(job.MetadataId, input);
                        return Answer(new RemoteJobResponse(job.MetadataId));
                    }
                    catch (Exception ex)
                    {
                        return Answer(
                            new RemoteJobResponse(
                                job.MetadataId,
                                IsError: true,
                                ErrorMessage: ex.Message,
                                ExceptionType: ex.GetType().Name
                            )
                        );
                    }
            }
        }

        private async Task RunJob(long metadataId, DeliveryProbeInput input)
        {
            await Task.Yield();
            using var scope = Services.CreateScope();
            var runner = scope.ServiceProvider.GetRequiredService<IJobRunnerTrain>();
            await runner.Run(new RunJobRequest(metadataId, input));
        }

        private static HttpResponseMessage Answer(RemoteJobResponse response) =>
            new(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    JsonSerializer.Serialize(response),
                    Encoding.UTF8,
                    "application/json"
                ),
            };
    }
}
