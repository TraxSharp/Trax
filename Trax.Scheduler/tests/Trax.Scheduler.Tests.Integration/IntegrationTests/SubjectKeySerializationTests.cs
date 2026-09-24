using System.Diagnostics;
using FluentAssertions;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.SqlDialect;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Logging.Extensions;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.JobSubmitter;
using Trax.Scheduler.Tests.ArrayLogger.Services.ArrayLoggingProvider;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.JobDispatcher;
using Trax.Scheduler.Trains.JobDispatcher.Junctions;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// Dispatch must not run two entries that name the same subject at the same time.
///
/// <para>
/// "Busy" means a dispatched entry for that subject whose run has not finished — the same
/// definition <c>DormantDependentContext</c> uses for an active execution. The submitter here is a
/// fake that records calls without running anything, so a dispatched entry's metadata stays
/// <c>Pending</c> and the subject stays busy until a test says otherwise.
/// </para>
///
/// <para>Enforces Trax.Docs/adr/0019-queued-work-for-one-subject-runs-one-at-a-time.md and Trax.Docs/adr/0018-a-deferred-enqueue-is-staged-and-a-stranded-one-is-cancelled.md.</para>
/// </summary>
[Property("adr", "Trax.Docs/adr/0019-queued-work-for-one-subject-runs-one-at-a-time.md")]
[Property(
    "adr",
    "Trax.Docs/adr/0018-a-deferred-enqueue-is-staged-and-a-stranded-one-is-cancelled.md"
)]
[TestFixture]
public class SubjectKeySerializationTests
{
    private static readonly string TestTrainName = typeof(SchedulerTestTrain).FullName!;

    /// <summary>
    /// The ceiling on waiting for a dispatcher or a hand-written claim to finish once the test has
    /// released it. It sits above Npgsql's 30 second default command timeout, the one inherited
    /// ceiling those awaits pass through, so a claim stuck on a lock fails with its own error
    /// rather than with this deadline.
    /// </summary>
    private static readonly TimeSpan ReleasedWorkBudget = TimeSpan.FromSeconds(60);

    private ServiceProvider _serviceProvider = null!;
    private ArrayLoggingProvider _logs = null!;
    private IServiceScope _scope = null!;
    private IDataContext _dataContext = null!;
    private ParallelDispatchTests.DelayingJobSubmitter _submitter = null!;
    private SchedulerConfiguration _schedulerConfiguration = null!;

    [OneTimeSetUp]
    public async Task RunBeforeAnyTests()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .Build();
        var connectionString = configuration.GetRequiredSection("Configuration")[
            "DatabaseConnectionString"
        ]!;

        _submitter = new ParallelDispatchTests.DelayingJobSubmitter(TimeSpan.FromMilliseconds(1));
        var arrayLoggingProvider = new ArrayLoggingProvider();
        _logs = arrayLoggingProvider;

        _serviceProvider = new ServiceCollection()
            .AddSingleton<ILoggerProvider>(arrayLoggingProvider)
            .AddSingleton<IArrayLoggingProvider>(arrayLoggingProvider)
            .AddLogging(x => x.AddConsole().SetMinimumLevel(LogLevel.Debug))
            .AddSingleton(_submitter)
            .AddTrax(trax =>
                trax.AddEffects(effects =>
                        effects
                            .SetEffectLogLevel(LogLevel.Information)
                            .SaveTrainParameters()
                            .UsePostgres(connectionString)
                            .AddDataContextLogging(minimumLogLevel: LogLevel.Trace)
                            .AddJson()
                            .AddJunctionLogger(serializeJunctionData: true)
                    )
                    .AddMediator(
                        typeof(AssemblyMarker).Assembly,
                        typeof(Scheduler.Trains.JobRunner.JobRunnerTrain).Assembly
                    )
                    .AddScheduler(scheduler =>
                        scheduler.OverrideSubmitter(s =>
                            s.AddScoped<IJobSubmitter>(sp =>
                                sp.GetRequiredService<ParallelDispatchTests.DelayingJobSubmitter>()
                            )
                        )
                    )
            )
            .AddScoped<IDataContext>(sp =>
            {
                var factory = sp.GetRequiredService<IDataContextProviderFactory>();
                return (IDataContext)factory.Create();
            })
            .BuildServiceProvider();

        _schedulerConfiguration = _serviceProvider.GetRequiredService<SchedulerConfiguration>();
    }

    [OneTimeTearDown]
    public async Task RunAfterAnyTests()
    {
        await _serviceProvider.DisposeAsync();
    }

    [SetUp]
    public async Task TestSetUp()
    {
        _scope = _serviceProvider.CreateScope();
        _dataContext = _scope.ServiceProvider.GetRequiredService<IDataContext>();
        await TestSetup.CleanupDatabase(_dataContext);
        _submitter.Reset();
    }

    [TearDown]
    public async Task TestTearDown()
    {
        // Reset to default after each test
        _schedulerConfiguration.MaxConcurrentDispatch = 1;
        _schedulerConfiguration.MaxActiveJobs = new SchedulerConfiguration().MaxActiveJobs;
        _schedulerConfiguration.MaxQueuedJobsPerCycle =
            new SchedulerConfiguration().MaxQueuedJobsPerCycle;

        if (_dataContext is IDisposable disposable)
            disposable.Dispose();
        _scope.Dispose();
    }

    // ── the guarantee ───────────────────────────────────────────────

    [Test]
    public async Task Two_entries_with_the_same_subject_key_are_not_dispatched_together()
    {
        await CreateEntry("customer-1", "first");
        await CreateEntry("customer-1", "second");

        await RunCycle();

        (await DispatchedCount())
            .Should()
            .Be(1, "the second entry names a subject whose run has not finished");
    }

    [Test]
    public async Task Entries_with_different_subject_keys_are_both_dispatched()
    {
        await CreateEntry("customer-1", "a");
        await CreateEntry("customer-2", "b");

        await RunCycle();

        (await DispatchedCount())
            .Should()
            .Be(
                2,
                "different subjects do not block each other — the guard must not serialise everything"
            );
    }

    [Test]
    public async Task Entries_without_a_subject_key_are_unaffected()
    {
        await CreateEntry(null, "a");
        await CreateEntry(null, "b");

        await RunCycle();

        (await DispatchedCount()).Should().Be(2, "a null key means no serialization");
    }

    [Test]
    public async Task A_blocked_entry_stays_queued_rather_than_being_skipped_or_lost()
    {
        var first = await CreateEntry("customer-1", "first");
        var second = await CreateEntry("customer-1", "second");

        await RunCycle();

        var blocked = await EntryAsync(second.Id);
        blocked!
            .Status.Should()
            .Be(WorkQueueStatus.Queued, "a blocked entry must remain claimable on a later cycle");
        blocked.ConfirmedAt.Should().NotBeNull();
        (await EntryAsync(first.Id))!.Status.Should().Be(WorkQueueStatus.Dispatched);
    }

    [Test]
    public async Task A_blocked_entry_is_dispatched_once_the_first_run_finishes()
    {
        await CreateEntry("customer-1", "first");
        var second = await CreateEntry("customer-1", "second");

        await RunCycle();
        await CompleteAllRuns();
        await RunCycle();

        (await EntryAsync(second.Id))!
            .Status.Should()
            .Be(
                WorkQueueStatus.Dispatched,
                "the subject was released when its run reached a terminal state"
            );
    }

    [Test]
    public async Task Entries_for_one_subject_at_equal_priority_are_dispatched_oldest_first()
    {
        var older = await CreateEntry(
            "customer-1",
            "older",
            createdAt: DateTime.UtcNow.AddMinutes(-5)
        );
        var newer = await CreateEntry("customer-1", "newer", createdAt: DateTime.UtcNow);

        await RunCycle();

        (await EntryAsync(older.Id))!.Status.Should().Be(WorkQueueStatus.Dispatched);
        (await EntryAsync(newer.Id))!.Status.Should().Be(WorkQueueStatus.Queued);
    }

    [Test]
    public async Task A_subject_blocked_by_a_stale_run_is_released_when_that_run_is_reaped()
    {
        await CreateEntry("customer-1", "first");
        var second = await CreateEntry("customer-1", "second");

        await RunCycle();

        // What ReapStaleInProgressMetadataJunction does to a run that never reported back.
        await FailAllRuns();
        await RunCycle();

        (await EntryAsync(second.Id))!
            .Status.Should()
            .Be(
                WorkQueueStatus.Dispatched,
                "a subject is blocked for at most the stale-reap window, not forever"
            );
    }

    // ── capacity: entries the claim will refuse must not use it up ─────

    [TestCase(
        true,
        TestName = "A_backlog_for_one_subject_does_not_starve_another (group-fair load)"
    )]
    [TestCase(false, TestName = "A_backlog_for_one_subject_does_not_starve_another (load all)")]
    public async Task A_backlog_for_one_subject_does_not_starve_another(bool groupFair)
    {
        UseLoadPath(groupFair);
        _schedulerConfiguration.MaxActiveJobs = 2;

        for (var i = 0; i < 4; i++)
            await CreateEntry(
                "customer-1",
                $"c1-{i}",
                createdAt: DateTime.UtcNow.AddMinutes(-10 + i)
            );
        var other = await CreateEntry("customer-2", "c2");

        await RunCycle();
        await RunCycle();

        (await EntryAsync(other.Id))!
            .Status.Should()
            .Be(
                WorkQueueStatus.Dispatched,
                "customer-1's queued siblings can never be claimed while its first run is in "
                    + "flight, so they must not be the candidates that fill the free slot"
            );
        (await DispatchedCount())
            .Should()
            .Be(2, "one run per subject, and capacity for both subjects");
    }

    [TestCase(true, TestName = "Stranded_staged_entries_do_not_take_capacity (group-fair load)")]
    [TestCase(false, TestName = "Stranded_staged_entries_do_not_take_capacity (load all)")]
    public async Task Stranded_staged_entries_do_not_take_capacity(bool groupFair)
    {
        UseLoadPath(groupFair);
        _schedulerConfiguration.MaxActiveJobs = 2;

        // Older than the ready entry, so without the filter they sort first and take both slots.
        await CreateEntry(
            null,
            "staged-1",
            createdAt: DateTime.UtcNow.AddMinutes(-10),
            staged: true
        );
        await CreateEntry(
            null,
            "staged-2",
            createdAt: DateTime.UtcNow.AddMinutes(-9),
            staged: true
        );
        var ready = await CreateEntry(null, "ready");

        await RunCycle();

        (await EntryAsync(ready.Id))!
            .Status.Should()
            .Be(
                WorkQueueStatus.Dispatched,
                "an unconfirmed entry can never be claimed, so it must not be a candidate"
            );
    }

    private void UseLoadPath(bool groupFair) =>
        _schedulerConfiguration.MaxQueuedJobsPerCycle = groupFair ? 100 : null;

    [Test]
    public async Task Two_dispatchers_claiming_sibling_entries_at_once_are_serialized()
    {
        // Driving this through two dispatcher cycles does not reach the race: a cycle dispatches
        // its candidates one at a time, so the second claim already sees the first as dispatched.
        // The race is two dispatchers in the claim window simultaneously, with both entries still
        // queued and neither committed — so it is forced here with two explicit transactions.
        var first = await CreateEntry("customer-1", "first");
        var second = await CreateEntry("customer-1", "second");

        using var scopeA = _serviceProvider.CreateScope();
        using var scopeB = _serviceProvider.CreateScope();
        var contextA = scopeA.ServiceProvider.GetRequiredService<IDataContext>();
        var contextB = scopeB.ServiceProvider.GetRequiredService<IDataContext>();
        var dialect = _serviceProvider.GetRequiredService<ISqlDialect>();

        using var txA = await contextA.BeginTransaction(CancellationToken.None);
        await Lock(contextA, dialect, "customer-1");
        var claimedA = await Claim(contextA, dialect, first.Id);
        claimedA.Should().NotBeNull("nothing is in flight yet, so the first claim succeeds");

        // A commits the claim the way the dispatcher does: the entry becomes dispatched with a
        // Pending run attached. B is started before that commit.
        var runA = await MarkDispatched(contextA, claimedA!);

        var b = Task.Run(async () =>
        {
            using var txB = await contextB.BeginTransaction(CancellationToken.None);
            await Lock(contextB, dialect, "customer-1");
            var claimedB = await Claim(contextB, dialect, second.Id);
            await contextB.CommitTransaction();
            return claimedB;
        });

        // Wait for B to actually be waiting on the subject lock, rather than guessing at a delay.
        await WaitUntilSomethingWaitsOnAnAdvisoryLock();

        await contextA.CommitTransaction();

        (await b.WaitAsync(ReleasedWorkBudget))
            .Should()
            .BeNull(
                "B waits on the subject lock, and once A commits it can see A's run in flight — "
                    + "without the lock both entries are still queued when B looks and both claim"
            );
        runA.Should().BeGreaterThan(0);
    }

    [Test]
    public async Task Two_dispatch_junctions_given_sibling_entries_at_once_dispatch_only_one()
    {
        // The test above proves the dialect's lock serialises two hand-written transactions. This
        // one proves the dispatcher actually takes it. Pausing the submitter would not reach the
        // race: submission happens after the claim commits, and by then the sibling can already
        // see the first run in flight. The window is inside the claim transaction, so the pause is
        // put there: the test holds a SHARE lock on the metadata table, which stops a dispatcher
        // at the metadata insert that follows its claim, before anything is committed.
        var first = await CreateEntry("customer-1", "first");
        var second = await CreateEntry("customer-1", "second");

        using var gateScope = _serviceProvider.CreateScope();
        var gate = gateScope.ServiceProvider.GetRequiredService<IDataContext>();
        // The pid is read inside the transaction: outside one, a pooled context may run each
        // command on a different connection.
        using var gateTransaction = await gate.BeginTransaction(CancellationToken.None);
        var gatePid = await ((DbContext)gate)
            .Database.SqlQueryRaw<int>("SELECT pg_backend_pid() AS \"Value\"")
            .FirstAsync();
        await ((DbContext)gate).Database.ExecuteSqlRawAsync(
            "LOCK TABLE trax.metadata IN SHARE MODE"
        );

        // DispatchJobsJunction catches and logs a failed entry rather than throwing, so its errors
        // are the only account of why an entry ended in the wrong state.
        _logs.ClearAllLogs();

        // A claims the first entry and stops at its metadata insert, holding its claim open.
        var dispatcherA = Task.Run(() => DispatchAlone(first));
        await WaitUntilBlockedBehindGate(gatePid, backends: 1);

        // B goes for the sibling while A's claim is uncommitted. With the subject lock it waits
        // behind A; without it, it claims the sibling too and stops at the same insert.
        var dispatcherB = Task.Run(() => DispatchAlone(second));
        await WaitUntilBlockedBehindGate(gatePid, backends: 2);

        await gate.CommitTransaction();

        try
        {
            await Task.WhenAll(dispatcherA, dispatcherB).WaitAsync(ReleasedWorkBudget);
        }
        catch (TimeoutException)
        {
            Assert.Fail(
                $"The dispatchers did not finish within {ReleasedWorkBudget} of the gate opening. "
                    + $"Dispatcher errors: {DispatcherErrors()}"
            );
        }

        (await EntryAsync(first.Id))!
            .Status.Should()
            .Be(WorkQueueStatus.Dispatched, "dispatcher errors: {0}", DispatcherErrors());
        (await EntryAsync(second.Id))!
            .Status.Should()
            .Be(
                WorkQueueStatus.Queued,
                "the dispatcher locks the subject before claiming, so B only looks once A has "
                    + "committed and sees A's run in flight — without the lock both claims succeed. "
                    + "Dispatcher errors: {0}",
                DispatcherErrors()
            );
        (await DispatchedCount()).Should().Be(1);
    }

    /// <summary>
    /// The errors <see cref="DispatchJobsJunction"/> logged since the test cleared the logs, or
    /// "none". Read while a timed-out dispatcher may still be writing, so a snapshot that races a
    /// write is retried rather than trusted.
    /// </summary>
    private string DispatcherErrors()
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var errors = _logs
                    .Loggers.ToList()
                    .SelectMany(l => l.Logs.ToList())
                    .Where(l =>
                        l.Category == typeof(DispatchJobsJunction).FullName
                        && l.Level >= LogLevel.Error
                    )
                    .Select(l => $"{l.Message} {l.Exception}")
                    .ToList();
                return errors.Count == 0 ? "none" : string.Join(Environment.NewLine, errors);
            }
            catch (InvalidOperationException) { }
        }

        return "unreadable (the log was still being written)";
    }

    /// <summary>Runs one dispatch junction over a single entry, as one dispatcher would.</summary>
    private async Task DispatchAlone(WorkQueue entry)
    {
        using var scope = _serviceProvider.CreateScope();
        var junction = ActivatorUtilities.CreateInstance<DispatchJobsJunction>(
            scope.ServiceProvider
        );
        await junction.Run([entry]);
    }

    /// <summary>
    /// Polls until the given number of backends are stuck behind the gate transaction, either
    /// directly or behind a backend that is. Synchronises on the lock state rather than a delay.
    /// </summary>
    private async Task WaitUntilBlockedBehindGate(int gatePid, int backends)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (DateTime.UtcNow < deadline)
        {
            using var probe = _serviceProvider.CreateScope();
            var context = (DbContext)probe.ServiceProvider.GetRequiredService<IDataContext>();

            var blocked = await context
                .Database.SqlQueryRaw<int>(
                    """
                    WITH direct AS (
                        SELECT pid FROM pg_stat_activity
                        WHERE datname = current_database() AND {0} = ANY(pg_blocking_pids(pid))
                    ),
                    indirect AS (
                        SELECT pid FROM pg_stat_activity
                        WHERE datname = current_database()
                          AND pg_blocking_pids(pid) && ARRAY(SELECT pid FROM direct)
                    )
                    SELECT ((SELECT count(*) FROM direct) + (SELECT count(*) FROM indirect))::int AS "Value"
                    """,
                    gatePid
                )
                .FirstAsync();

            if (blocked >= backends)
                return;

            await Task.Yield();
        }

        Assert.Fail(
            $"Fewer than {backends} dispatcher(s) ever blocked behind the test's gate. "
                + $"Dispatcher errors: {DispatcherErrors()}"
        );
    }

    /// <summary>
    /// Polls until a transaction is blocked on an advisory lock, so the test synchronises on the
    /// condition it actually cares about instead of a fixed delay.
    /// </summary>
    private async Task WaitUntilSomethingWaitsOnAnAdvisoryLock()
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);

        while (DateTime.UtcNow < deadline)
        {
            using var probe = _serviceProvider.CreateScope();
            var context = (DbContext)probe.ServiceProvider.GetRequiredService<IDataContext>();

            var waiting = await context
                .Database.SqlQueryRaw<int>(
                    """
                    SELECT count(*)::int AS "Value" FROM pg_locks
                    WHERE locktype = 'advisory' AND NOT granted
                      AND database = (SELECT oid FROM pg_database WHERE datname = current_database())
                    """
                )
                .FirstAsync();

            if (waiting > 0)
                return;

            await Task.Yield();
        }

        Assert.Fail("No transaction ever blocked on the subject lock — the lock was not taken.");
    }

    private static async Task Lock(IDataContext context, ISqlDialect dialect, string subject)
    {
        await ((DbContext)context).Database.ExecuteSqlRawAsync(
            dialect.LockSubject(),
            [subject],
            CancellationToken.None
        );
    }

    private static Task<WorkQueue?> Claim(IDataContext context, ISqlDialect dialect, long id) =>
        context
            .WorkQueues.FromSqlRaw(dialect.ClaimWorkQueueEntry(), id)
            .FirstOrDefaultAsync(CancellationToken.None);

    /// <summary>Mirrors what the dispatcher does after a successful claim.</summary>
    private static async Task<long> MarkDispatched(IDataContext context, WorkQueue claimed)
    {
        var metadata = Trax.Effect.Models.Metadata.Metadata.Create(
            new Trax.Effect.Models.Metadata.DTOs.CreateMetadata
            {
                Name = claimed.TrainName!,
                ExternalId = claimed.ExternalId,
                Input = null,
            }
        );
        await context.Track(metadata);
        await context.SaveChanges(CancellationToken.None);

        claimed.Status = WorkQueueStatus.Dispatched;
        claimed.MetadataId = metadata.Id;
        claimed.DispatchedAt = DateTime.UtcNow;
        await context.SaveChanges(CancellationToken.None);
        return metadata.Id;
    }

    // ── helpers ─────────────────────────────────────────────────────

    private async Task RunCycle()
    {
        using var scope = _serviceProvider.CreateScope();
        var train = scope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>();
        await train.Run(Unit.Default);
        if (train is IDisposable d)
            d.Dispose();
    }

    private async Task<WorkQueue> CreateEntry(
        string? subjectKey,
        string value,
        DateTime? createdAt = null,
        bool staged = false
    )
    {
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = TestTrainName,
                Input = System.Text.Json.JsonSerializer.Serialize(
                    new SchedulerTestInput { Value = value }
                ),
                InputTypeName = typeof(SchedulerTestInput).FullName,
                SubjectKey = subjectKey,
                DeferPromotion = staged,
            }
        );

        if (createdAt is not null)
            entry.CreatedAt = createdAt.Value;

        await _dataContext.Track(entry);
        await _dataContext.SaveChanges(CancellationToken.None);
        return entry;
    }

    private async Task<WorkQueue?> EntryAsync(long id)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IDataContext>();
        return await context.WorkQueues.AsNoTracking().FirstOrDefaultAsync(w => w.Id == id);
    }

    private async Task<int> DispatchedCount()
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IDataContext>();
        return await context.WorkQueues.CountAsync(w => w.Status == WorkQueueStatus.Dispatched);
    }

    private Task CompleteAllRuns() => SetAllRunsTo(TrainState.Completed);

    private Task FailAllRuns() => SetAllRunsTo(TrainState.Failed);

    private async Task SetAllRunsTo(TrainState state)
    {
        using var scope = _serviceProvider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IDataContext>();
        await context
            .Metadatas.Where(m =>
                m.TrainState == TrainState.Pending || m.TrainState == TrainState.InProgress
            )
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.TrainState, state));
    }
}
