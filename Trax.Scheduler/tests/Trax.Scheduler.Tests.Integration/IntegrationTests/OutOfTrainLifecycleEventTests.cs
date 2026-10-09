using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Trax.Core.Exceptions;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.Services.TrainLifecycleHook;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Services.ManifestManagerPollingService;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Services.SchedulerStartupService;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.JobDispatcher;
using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// A run the scheduler moves to <c>Failed</c> or <c>Cancelled</c> itself, with no train running
/// to publish the outcome, still reaches the lifecycle hooks, once: a subscriber following the run
/// is told it ended. Each path publishes only the runs its own write moved, so a second pass over
/// the same rows publishes nothing.
/// </summary>
/// <remarks>
/// Every publish is awaited by the path that makes it, so each assertion follows the call that
/// published, with no waiting.
/// </remarks>
[TestFixture]
[NonParallelizable]
public class OutOfTrainLifecycleEventTests
{
    private static readonly string TrainName = typeof(ISchedulerTestTrain).FullName!;

    private ServiceProvider _provider = null!;
    private readonly LifecycleRecorder _lifecycle = new();

    [OneTimeSetUp]
    public void RunBeforeAnyTests()
    {
        _provider = new ServiceCollection()
            .AddLogging(x => x.AddConsole().SetMinimumLevel(LogLevel.Information))
            .AddTrax(trax =>
                trax.AddEffects(effects =>
                        effects
                            .SaveTrainParameters()
                            .UsePostgres(TestPostgres.ConnectionString)
                            .AddJson()
                            .AddLifecycleHook<RecordingLifecycleHook>()
                    )
                    .AddMediator(typeof(AssemblyMarker).Assembly, typeof(JobRunnerTrain).Assembly)
                    .AddScheduler(scheduler =>
                        scheduler.OverrideSubmitter(s =>
                            s.AddScoped<IJobSubmitter, UnreachableSubmitter>()
                        )
                    )
            )
            .AddSingleton(_lifecycle)
            .AddScoped<IDataContext>(sp =>
                (IDataContext)sp.GetRequiredService<IDataContextProviderFactory>().Create()
            )
            .BuildServiceProvider();

        var config = _provider.GetRequiredService<SchedulerConfiguration>();
        config.StalePendingTimeout = TimeSpan.FromMinutes(1);
        config.StaleInProgressTimeout = TimeSpan.FromMinutes(1);
    }

    [OneTimeTearDown]
    public async Task RunAfterAnyTests() => await _provider.DisposeAsync();

    [SetUp]
    public async Task TestSetUp()
    {
        using var scope = _provider.CreateScope();
        await TestSetup.CleanupDatabase(scope.ServiceProvider.GetRequiredService<IDataContext>());
        _lifecycle.Clear();
    }

    [Test]
    public async Task A_reaped_InProgress_run_and_a_reaped_Pending_run_each_publish_Failed_once()
    {
        var stalePending = await SeedRunAsync(TrainState.Pending, DateTime.UtcNow.AddHours(-1));
        var staleInProgress = await SeedRunAsync(
            TrainState.InProgress,
            DateTime.UtcNow.AddHours(-1)
        );

        // The real leader path: the reapers write inside the leader transaction, and their events
        // go out once it commits.
        var polling = new ManifestManagerPollingService(
            _provider,
            _provider.GetRequiredService<SchedulerConfiguration>(),
            NullLogger<ManifestManagerPollingService>.Instance,
            _provider.GetRequiredService<ISqlDialect>()
        );

        await polling.RunManifestManager(CancellationToken.None);

        _lifecycle
            .Events.Should()
            .BeEquivalentTo([
                ("Failed", stalePending, TrainState.Failed),
                ("StateChanged", stalePending, TrainState.Failed),
                ("Failed", staleInProgress, TrainState.Failed),
                ("StateChanged", staleInProgress, TrainState.Failed),
            ]);
        _lifecycle
            .FailureTypes.Should()
            .BeEquivalentTo(
                ["StalePendingTimeout", "StaleInProgressTimeout"],
                "the event carries the failure the reaper recorded, as a train's failure does"
            );

        await polling.RunManifestManager(CancellationToken.None);

        _lifecycle
            .Events.Should()
            .HaveCount(4, "a second pass finds both runs already Failed and publishes nothing");
    }

    [Test]
    public async Task The_runs_a_reaper_failed_are_published_side_by_side_each_once()
    {
        var stale = new List<long>();
        for (var i = 0; i < 4; i++)
            stale.Add(await SeedRunAsync(TrainState.InProgress, DateTime.UtcNow.AddHours(-1)));

        // Each OnFailed waits for a second one to be in flight with it, or gives up after a while.
        _lifecycle.MeetOnFailed(TimeSpan.FromSeconds(5));

        var polling = new ManifestManagerPollingService(
            _provider,
            _provider.GetRequiredService<SchedulerConfiguration>(),
            NullLogger<ManifestManagerPollingService>.Instance,
            _provider.GetRequiredService<ISqlDialect>()
        );
        await polling.RunManifestManager(CancellationToken.None);

        _lifecycle
            .Alone.Should()
            .Be(
                0,
                "a reaper's runs are published several at a time, so one slow hook does not hold up the rest"
            );
        _lifecycle
            .Events.Where(e => e.Event == "Failed")
            .Select(e => e.Id)
            .Should()
            .BeEquivalentTo(stale, "the one statement that failed them all published each once");
    }

    [Test]
    public async Task An_operator_cancel_of_a_pending_run_publishes_Cancelled_once()
    {
        var pending = await SeedRunAsync(TrainState.Pending, DateTime.UtcNow);

        using (var scope = _provider.CreateScope())
        {
            var result = await scope
                .ServiceProvider.GetRequiredService<IOperationsService>()
                .CancelExecutionsAsync([pending], CancellationToken.None);
            result.Success.Should().BeTrue(result.Message);
        }

        _lifecycle
            .Events.Should()
            .BeEmpty("the cancel only flags the run; it ends when a runner reaches it");

        // The job reaches a runner, which records it Cancelled without running it; then a second
        // delivery of the same job finds it Cancelled already.
        for (var delivery = 0; delivery < 2; delivery++)
        {
            using var scope = _provider.CreateScope();
            await scope
                .ServiceProvider.GetRequiredService<IJobRunnerTrain>()
                .Run(new RunJobRequest(pending, new SchedulerTestInput { Value = "x" }));
        }

        _lifecycle
            .Events.Should()
            .Equal(
                ("Cancelled", pending, TrainState.Cancelled),
                ("StateChanged", pending, TrainState.Cancelled)
            );
    }

    [Test]
    public async Task A_run_failed_by_startup_recovery_publishes_Failed_once()
    {
        var stuck = await SeedRunAsync(TrainState.InProgress, DateTime.UtcNow.AddMinutes(-5));

        var configuration = new SchedulerConfiguration
        {
            RecoverStuckJobsOnStartup = true,
            PruneOrphanedManifests = false,
            HasDatabaseProvider = true,
        };

        for (var start = 0; start < 2; start++)
        {
            using var scope = _provider.CreateScope();
            await new SchedulerStartupService(
                scope.ServiceProvider,
                configuration,
                NullLogger<SchedulerStartupService>.Instance
            ).StartAsync(CancellationToken.None);
        }

        _lifecycle
            .Events.Should()
            .Equal(
                ("Failed", stuck, TrainState.Failed),
                ("StateChanged", stuck, TrainState.Failed)
            );
        _lifecycle.FailureTypes.Should().Equal("ServerRestart");
    }

    [Test]
    public async Task A_run_whose_submit_fails_publishes_Failed_once_without_the_submitters_detail()
    {
        using var scope = _provider.CreateScope();
        var operations = scope.ServiceProvider.GetRequiredService<IOperationsService>();

        var run = () =>
            operations.RunTrainAsync(
                new RunTrainInput(TrainName, """{"value":"x"}"""),
                CancellationToken.None
            );

        await run.Should().ThrowAsync<HttpRequestException>();

        var failed = await StoredRunsAsync();
        failed.Should().ContainSingle().Which.TrainState.Should().Be(TrainState.Failed);
        var id = failed.Single().Id;

        _lifecycle
            .Events.Should()
            .Equal(("Failed", id, TrainState.Failed), ("StateChanged", id, TrainState.Failed));
        _lifecycle
            .FailureTypes.Should()
            .Equal(
                [DispatchFailure.Failed],
                "the submitter's detail stays on the row, as for a failed dispatch"
            );
        _lifecycle.FailureReasons.Should().NotContain(r => r.Contains("unreachable"));
    }

    private async Task<long> SeedRunAsync(TrainState state, DateTime startTime)
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IDataContext>();

        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = TrainName,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = null,
            }
        );
        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);

        await context
            .Metadatas.Where(m => m.Id == metadata.Id)
            .ExecuteUpdateAsync(s =>
                s.SetProperty(m => m.TrainState, state).SetProperty(m => m.StartTime, startTime)
            );

        return metadata.Id;
    }

    private async Task<List<Metadata>> StoredRunsAsync()
    {
        using var scope = _provider.CreateScope();
        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .Metadatas.AsNoTracking()
            .Where(m => m.Name == TrainName)
            .ToListAsync();
    }

    /// <summary>
    /// The lifecycle events of the test train's runs, across every scope. The scheduler's own
    /// trains publish their own events, which are left out.
    /// </summary>
    private sealed class LifecycleRecorder
    {
        private readonly List<(string Event, long Id, TrainState State)> _events = [];
        private readonly List<(string? Type, string? Reason)> _failures = [];

        public IReadOnlyList<(string Event, long Id, TrainState State)> Events
        {
            get
            {
                lock (_events)
                    return _events.ToList();
            }
        }

        /// <summary>The failure type each <c>OnFailed</c> exception carried.</summary>
        public IReadOnlyList<string?> FailureTypes
        {
            get
            {
                lock (_failures)
                    return _failures.Select(f => f.Type).ToList();
            }
        }

        /// <summary>The failure reason each <c>OnFailed</c> run showed its hooks.</summary>
        public IReadOnlyList<string> FailureReasons
        {
            get
            {
                lock (_failures)
                    return _failures.Select(f => f.Reason ?? string.Empty).ToList();
            }
        }

        public void Add(string name, Metadata metadata)
        {
            if (metadata.Name != TrainName)
                return;
            lock (_events)
                _events.Add((name, metadata.Id, metadata.TrainState));
        }

        public void AddFailure(Metadata metadata, Exception exception)
        {
            if (metadata.Name != TrainName)
                return;
            var data = exception.Data["TrainExceptionData"] as TrainExceptionData;
            lock (_failures)
                _failures.Add((data?.Type, metadata.FailureReason));
        }

        private TimeSpan? _meetWithin;
        private TaskCompletionSource _met = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _inFlight;
        private int _alone;

        /// <summary>How many <c>OnFailed</c> calls met no other in flight with them.</summary>
        public int Alone => Volatile.Read(ref _alone);

        /// <summary>Makes each <c>OnFailed</c> wait, up to <paramref name="within"/>, for a second one in flight.</summary>
        public void MeetOnFailed(TimeSpan within) => _meetWithin = within;

        public async Task MeetAsync()
        {
            if (_meetWithin is not { } within)
                return;
            if (Interlocked.Increment(ref _inFlight) >= 2)
                _met.TrySetResult();
            try
            {
                await _met.Task.WaitAsync(within);
            }
            catch (TimeoutException)
            {
                Interlocked.Increment(ref _alone);
            }
        }

        public void Clear()
        {
            lock (_events)
                _events.Clear();
            lock (_failures)
                _failures.Clear();
            _meetWithin = null;
            _met = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            _inFlight = 0;
            _alone = 0;
        }
    }

    private sealed class RecordingLifecycleHook(LifecycleRecorder recorder) : ITrainLifecycleHook
    {
        public Task OnStarted(Metadata metadata, CancellationToken ct) =>
            Record("Started", metadata);

        public Task OnCompleted(Metadata metadata, CancellationToken ct) =>
            Record("Completed", metadata);

        public async Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct)
        {
            if (metadata.Name == TrainName)
                await recorder.MeetAsync();
            recorder.AddFailure(metadata, exception);
            await Record("Failed", metadata);
        }

        public Task OnCancelled(Metadata metadata, CancellationToken ct) =>
            Record("Cancelled", metadata);

        public Task OnStateChanged(Metadata metadata, CancellationToken ct) =>
            Record("StateChanged", metadata);

        private Task Record(string name, Metadata metadata)
        {
            recorder.Add(name, metadata);
            return Task.CompletedTask;
        }
    }

    /// <summary>A submitter whose remote worker cannot be reached.</summary>
    private sealed class UnreachableSubmitter : IJobSubmitter
    {
        private const string Detail = "Simulated submit failure: remote worker unreachable";

        public Task<string> EnqueueAsync(long metadataId) => throw new HttpRequestException(Detail);

        public Task<string> EnqueueAsync(long metadataId, object input) =>
            throw new HttpRequestException(Detail);

        public Task<string> EnqueueAsync(long metadataId, CancellationToken cancellationToken) =>
            throw new HttpRequestException(Detail);

        public Task<string> EnqueueAsync(
            long metadataId,
            object input,
            CancellationToken cancellationToken
        ) => throw new HttpRequestException(Detail);
    }
}
