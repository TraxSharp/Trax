using System.Diagnostics;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using Trax.Core.Functional;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Mediator.Services.TrainExecution;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobDispatcherPollingService;
using Trax.Scheduler.Services.QueuedWorkListenerService;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.JobDispatcher;
using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// On Postgres the job dispatcher wakes when another host commits queued work, rather than waiting
/// for its next poll, and hears nothing from a transaction that rolls back.
/// </summary>
/// <remarks>
/// Each test runs a real dispatcher host polling every 30 seconds, and queues from a second
/// service provider with its own data source, the way an API host would. Every wait is on a
/// signal (a dispatcher cycle, a subscription, a notice) with a timeout the test owns.
/// </remarks>
[TestFixture]
public class DispatcherWakeTests
{
    private static readonly TimeSpan Poll = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan SetupTimeout = TimeSpan.FromSeconds(20);

    private const string ListenerApplicationName = "trax_queued_work_listener";
    private const string Channel = "trax_queued_work";

    private ServiceProvider _dispatcherHost = null!;
    private List<IHostedService> _running = [];
    private ServiceProvider _apiHost = null!;
    private readonly SignalCounter _cycles = new();
    private readonly SignalCounter _subscriptions = new();
    private readonly SignalCounter _notices = new();

    /// <summary>The dispatcher host's listener, which the host owns and disposes.</summary>
    private QueuedWorkListenerService Listener =>
        _dispatcherHost.GetRequiredService<QueuedWorkListenerService>();

    [SetUp]
    public async Task SetUp()
    {
        _apiHost = BuildApiHost();
        using (var scope = _apiHost.CreateScope())
            await TestSetup.CleanupDatabase(
                scope.ServiceProvider.GetRequiredService<IDataContext>()
            );

        _dispatcherHost = BuildDispatcherHost(_cycles);
        Listener.Subscribed += _subscriptions.Increment;
        Listener.NoticeReceived += _notices.Increment;

        // The two services under test, started as the host would start them. The rest of a
        // scheduler host (startup validation over every train in this assembly, the manifest
        // manager) has nothing to do with the wake.
        _running = _dispatcherHost
            .GetServices<IHostedService>()
            .Where(s => s is JobDispatcherPollingService or QueuedWorkListenerService)
            .ToList();
        _running.Should().HaveCount(2);
        foreach (var service in _running)
            await service.StartAsync(CancellationToken.None);

        // The startup cycle has run and the listener is listening, so whatever happens next is
        // something the test did.
        await _cycles.WhenReached(1).WaitAsync(SetupTimeout);
        await _subscriptions.WhenReached(1).WaitAsync(SetupTimeout);
    }

    [TearDown]
    public async Task TearDown()
    {
        foreach (var service in _running)
            await service.StopAsync(CancellationToken.None);
        await _dispatcherHost.DisposeAsync();
        await _apiHost.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
    }

    [Test]
    public async Task A_run_queued_on_another_host_starts_within_a_second_despite_a_30_second_poll()
    {
        // The first dispatch through a fresh host compiles its queries; that is not the latency
        // under test, so one run goes through first.
        var warmUp = await QueueFromApiHostAsync();
        await WaitUntilDispatchedAsync(warmUp, SetupTimeout);

        var queued = await QueueFromApiHostAsync();
        var sinceCommit = Stopwatch.StartNew();

        await WaitUntilDispatchedAsync(queued, Budget);

        sinceCommit
            .Elapsed.Should()
            .BeLessThan(Budget, "the commit wakes the dispatcher; its next poll is 30 s away");
    }

    [Test]
    public async Task A_rolled_back_enqueue_sends_no_notification()
    {
        // The test's own session on the channel: once a query on it returns, every notification
        // committed before that query began has reached it, so its count is exact.
        await using var oracle = new NpgsqlConnection(TestPostgres.ConnectionString);
        await oracle.OpenAsync();
        var oracleNotices = 0;
        oracle.Notification += (_, _) => Interlocked.Increment(ref oracleNotices);
        await ExecuteAsync(oracle, $"LISTEN {Channel}");

        var noticesBefore = Listener.Notices;

        using (var scope = _apiHost.CreateScope())
        {
            var dataContext = scope.ServiceProvider.GetRequiredService<IDataContext>();
            using var transaction = await dataContext.BeginTransaction();
            await dataContext.Track(
                WorkQueue.Create(
                    new CreateWorkQueue
                    {
                        TrainName = typeof(ISchedulerTestTrain).FullName!,
                        Input = "{}",
                        InputTypeName = typeof(SchedulerTestInput).FullName,
                    }
                )
            );
            await dataContext.SaveChanges(CancellationToken.None);
            await dataContext.RollbackTransaction();
        }

        // Committed after the rollback, so its notification is the only one that can exist.
        var sentinel = await QueueFromApiHostAsync();
        await ExecuteAsync(oracle, "SELECT 1");

        oracleNotices
            .Should()
            .Be(1, "only the committed enqueue notifies; the rolled back one never does");

        await _notices.WhenReached(noticesBefore + 1).WaitAsync(SetupTimeout);
        Listener.Notices.Should().Be(noticesBefore + 1);
        await WaitUntilDispatchedAsync(sentinel, SetupTimeout);
    }

    [Test]
    public async Task The_listener_reconnects_after_its_connection_is_terminated_and_wakes_again()
    {
        await using (var admin = new NpgsqlConnection(TestPostgres.ConnectionString))
        {
            await admin.OpenAsync();
            await using var terminate = new NpgsqlCommand(
                "SELECT count(pg_terminate_backend(pid)) FROM pg_stat_activity "
                    + "WHERE application_name = @name AND datname = current_database()",
                admin
            );
            terminate.Parameters.AddWithValue("name", ListenerApplicationName);
            var terminated = (long)(await terminate.ExecuteScalarAsync())!;
            terminated.Should().Be(1, "the dispatcher host holds one listening session");
        }

        // The first retry comes after a one-second backoff.
        await _subscriptions.WhenReached(2).WaitAsync(SetupTimeout);

        var noticesBefore = Listener.Notices;
        var queued = await QueueFromApiHostAsync();

        await _notices.WhenReached(noticesBefore + 1).WaitAsync(SetupTimeout);
        await WaitUntilDispatchedAsync(queued, SetupTimeout);
    }

    private async Task<long> QueueFromApiHostAsync()
    {
        using var scope = _apiHost.CreateScope();
        var execution = scope.ServiceProvider.GetRequiredService<ITrainExecutionService>();
        var result = await execution.QueueAsync(
            typeof(ISchedulerTestTrain).FullName!,
            "{}",
            ct: CancellationToken.None
        );
        return result.WorkQueueId;
    }

    /// <summary>
    /// Waits, cycle by cycle, until the entry is dispatched, and fails when that takes longer than
    /// <paramref name="timeout"/> from the call.
    /// </summary>
    private async Task WaitUntilDispatchedAsync(long workQueueId, TimeSpan timeout)
    {
        var elapsed = Stopwatch.StartNew();
        var next = _cycles.Count + 1;
        while (!await IsDispatchedAsync(workQueueId))
        {
            var remaining = timeout - elapsed.Elapsed;
            if (remaining <= TimeSpan.Zero)
                break;
            try
            {
                await _cycles.WhenReached(next).WaitAsync(remaining);
            }
            catch (TimeoutException)
            {
                break;
            }
            next = _cycles.Count + 1;
        }

        (await IsDispatchedAsync(workQueueId))
            .Should()
            .BeTrue($"work queue entry {workQueueId} should be dispatched within {timeout}");
    }

    private async Task<bool> IsDispatchedAsync(long workQueueId)
    {
        using var scope = _apiHost.CreateScope();
        var dataContext = scope.ServiceProvider.GetRequiredService<IDataContext>();
        var status = await dataContext
            .WorkQueues.AsNoTracking()
            .Where(w => w.Id == workQueueId)
            .Select(w => w.Status)
            .SingleAsync();
        return status == WorkQueueStatus.Dispatched;
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>A host that queues work and dispatches none, with a data source of its own.</summary>
    private static ServiceProvider BuildApiHost() =>
        new ServiceCollection()
            .AddLogging(b => b.AddProvider(NullLoggerProvider.Instance))
            .AddTrax(trax =>
                trax.AddEffects(effects =>
                        effects
                            .UsePostgres(TestPostgres.ConnectionString)
                            .AddJson()
                            .SaveTrainParameters()
                    )
                    .AddMediator(typeof(AssemblyMarker).Assembly)
            )
            .AddScoped<IDataContext>(sp =>
                (IDataContext)sp.GetRequiredService<IDataContextProviderFactory>().Create()
            )
            .BuildServiceProvider();

    /// <summary>
    /// A scheduler host polling every 30 seconds, whose dispatcher reports each finished cycle to
    /// <paramref name="cycles"/>.
    /// </summary>
    private static ServiceProvider BuildDispatcherHost(SignalCounter cycles)
    {
        var services = new ServiceCollection();
        services
            .AddLogging(b => b.AddProvider(NullLoggerProvider.Instance))
            .AddTrax(trax =>
                trax.AddEffects(effects =>
                        effects
                            .UsePostgres(TestPostgres.ConnectionString)
                            .AddDecisionRecording()
                            .AddJson()
                            .SaveTrainParameters()
                    )
                    .AddMediator(typeof(AssemblyMarker).Assembly, typeof(JobRunnerTrain).Assembly)
                    .AddScheduler(scheduler =>
                        scheduler
                            .UseInMemoryWorkers()
                            .JobDispatcherPollingInterval(Poll)
                            .ManifestManagerPollingInterval(Poll)
                    )
            );

        var dispatcher = services.Last(d => d.ServiceType == typeof(IJobDispatcherTrain));
        var create = dispatcher.ImplementationFactory!;
        services.Remove(dispatcher);
        services.AddScoped<IJobDispatcherTrain>(sp => new CountedDispatcher(
            (IJobDispatcherTrain)create(sp),
            cycles
        ));
        return services.BuildServiceProvider();
    }

    /// <summary>The real dispatcher train, reporting each finished cycle.</summary>
    private sealed class CountedDispatcher(IJobDispatcherTrain inner, SignalCounter cycles)
        : IJobDispatcherTrain
    {
        public Metadata? Metadata => inner.Metadata;

        public async Task<Unit> Run(Unit input, CancellationToken cancellationToken = default)
        {
            try
            {
                return await inner.Run(input, cancellationToken);
            }
            finally
            {
                cycles.Increment();
            }
        }

        public void Dispose() => inner.Dispose();
    }

    /// <summary>A count a test can wait on reaching a value.</summary>
    private sealed class SignalCounter
    {
        private readonly Lock _gate = new();
        private readonly List<(int Target, TaskCompletionSource Reached)> _waiters = [];
        private int _count;

        public int Count
        {
            get
            {
                lock (_gate)
                    return _count;
            }
        }

        public void Increment()
        {
            lock (_gate)
            {
                _count++;
                foreach (var waiter in _waiters.Where(w => w.Target <= _count).ToList())
                {
                    waiter.Reached.TrySetResult();
                    _waiters.Remove(waiter);
                }
            }
        }

        public Task WhenReached(int target)
        {
            lock (_gate)
            {
                if (_count >= target)
                    return Task.CompletedTask;
                var reached = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously
                );
                _waiters.Add((target, reached));
                return reached.Task;
            }
        }
    }
}
