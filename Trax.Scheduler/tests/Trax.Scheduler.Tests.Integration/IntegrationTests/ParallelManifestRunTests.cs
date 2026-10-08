using System.Collections.Concurrent;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Core.Monad;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.JunctionProvider.Progress.Extensions;
using Trax.Effect.Models.DeadLetter;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.Manifest.DTOs;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.FailureClassifier;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.TraxScheduler;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.JobDispatcher;
using Trax.Scheduler.Trains.JobRunner;
using Trax.Scheduler.Trains.ManifestManager;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// A manifest whose train runs its junctions in <c>Parallel</c> branches is scheduled like any
/// other: a branch's failure fails the run, which is retried after its backoff and dead-lettered
/// when its retries run out, and every retry or requeue replays each branch's own decisions. A run
/// past its job timeout while its branches run is recorded cancelled.
/// </summary>
[TestFixture]
[NonParallelizable]
public class ParallelManifestRunTests
{
    private ServiceProvider _provider = null!;

    // A host built for one test, in place of the shared one. Disposed by that test.
    private IServiceProvider? _override;

    private IServiceProvider Provider => _override ?? _provider;

    [OneTimeSetUp]
    public void BuildProvider() => _provider = BuildProvider(TimeSpan.Zero);

    private static ServiceProvider BuildProvider(TimeSpan retryDelay) =>
        new ServiceCollection()
            .AddLogging(x => x.SetMinimumLevel(LogLevel.Warning))
            .AddSingleton<IDecider>(ParallelProbe.Decider)
            .AddSingleton<IFailureClassifier, ParallelProbeClassifier>()
            .AddTrax(trax =>
                trax.AddEffects(effects =>
                        effects
                            .SaveTrainParameters()
                            .UsePostgres(TestPostgres.ConnectionString)
                            .AddDecisionRecording()
                            .AddJunctionProgress()
                            .AddJson()
                    )
                    .AddMediator(typeof(AssemblyMarker).Assembly, typeof(JobRunnerTrain).Assembly)
                    .AddScheduler(scheduler =>
                        scheduler.UseInMemoryWorkers().DefaultRetryDelay(retryDelay)
                    )
            )
            .AddScoped<IDataContext>(sp =>
                (IDataContext)sp.GetRequiredService<IDataContextProviderFactory>().Create()
            )
            .BuildServiceProvider();

    [OneTimeTearDown]
    public async Task DisposeProvider() => await _provider.DisposeAsync();

    [SetUp]
    public async Task Clean()
    {
        ParallelProbe.Reset();

        using var scope = Provider.CreateScope();
        await TestSetup.CleanupDatabase(scope.ServiceProvider.GetRequiredService<IDataContext>());
    }

    [TearDown]
    public void ResetProbe() => ParallelProbe.Reset();

    [Test]
    public async Task A_failed_branch_fails_the_run_and_its_retry_replays_each_branchs_decisions()
    {
        var manifest = await CreateManifestAsync("retried");

        ParallelProbe.Fail = true;
        var failed = await CycleAsync(manifest);

        failed.TrainState.Should().Be(TrainState.Failed);
        failed.FailureException.Should().Be(nameof(BranchesFailedException));
        failed
            .FailureClass.Should()
            .Be(
                FailureClass.Transient,
                "the classifier calls the failed branch's failure transient"
            );
        (await DecisionsOf(failed.Id))
            .Select(d => d.BranchPath)
            .Should()
            .BeEquivalentTo(["Parallel#0/fast", "Parallel#0/slow"]);
        var asked = ParallelProbe.Decider.Asked;

        // Asked now, the decider would answer the other way in every branch.
        ParallelProbe.Fail = false;
        ParallelProbe.Decider.Flip();
        var retry = await CycleAsync(manifest);

        retry.TrainState.Should().Be(TrainState.Completed, retry.FailureReason);
        retry.ReplayDecisionsOf.Should().Be(failed.Id);
        ParallelProbe.Decider.Asked.Should().Be(asked, "the retry asked nothing");
        ParallelProbe
            .TakenBy(retry.Id)
            .Should()
            .BeEquivalentTo(ParallelProbe.TakenBy(failed.Id), "each branch took its track again");
    }

    [Test]
    public async Task A_failed_parallel_run_is_retried_after_its_backoff()
    {
        await using var delayed = BuildProvider(TimeSpan.FromMinutes(10));
        _override = delayed;
        try
        {
            var manifest = await CreateManifestAsync("backoff");

            ParallelProbe.Fail = true;
            (await CycleAsync(manifest)).TrainState.Should().Be(TrainState.Failed);
            await RunManifestManagerAsync();

            var retry = await WithData(data =>
                data.WorkQueues.AsNoTracking()
                    .SingleAsync(q =>
                        q.ManifestId == manifest.Id && q.Status == WorkQueueStatus.Queued
                    )
            );
            retry
                .ScheduledAt.Should()
                .BeAfter(
                    DateTime.UtcNow.AddMinutes(9),
                    "a Parallel run's failure is a failed run, retried after the backoff"
                );
        }
        finally
        {
            _override = null;
        }
    }

    [Test]
    public async Task A_dead_lettered_parallel_run_requeued_replays_each_branchs_decisions()
    {
        var (manifest, failed) = await DeadLetteredAsync("dead");

        ParallelProbe.Decider.Flip();
        var asked = ParallelProbe.Decider.Asked;
        var replayed = await RequeueDeadLetterAndRunAsync(manifest, askAfresh: false);

        replayed.TrainState.Should().Be(TrainState.Completed, replayed.FailureReason);
        replayed.ReplayDecisionsOf.Should().Be(failed.Id);
        ParallelProbe.Decider.Asked.Should().Be(asked, "the requeue asked nothing");
        ParallelProbe
            .TakenBy(replayed.Id)
            .Should()
            .BeEquivalentTo(ParallelProbe.TakenBy(failed.Id));
    }

    [Test]
    public async Task A_dead_lettered_parallel_run_requeued_to_ask_afresh_asks_in_each_branch()
    {
        var (manifest, failed) = await DeadLetteredAsync("dead-fresh");

        ParallelProbe.Decider.Flip();
        var asked = ParallelProbe.Decider.Asked;
        var fresh = await RequeueDeadLetterAndRunAsync(manifest, askAfresh: true);

        fresh.TrainState.Should().Be(TrainState.Completed, fresh.FailureReason);
        fresh.ReplayDecisionsOf.Should().BeNull();
        (ParallelProbe.Decider.Asked - asked).Should().Be(2, "one asking in each branch");
        ParallelProbe
            .TakenBy(fresh.Id)
            .Should()
            .BeEquivalentTo(
                ParallelProbe.TakenBy(failed.Id).Select(t => (t.Branch, Flipped(t.Way)))
            );
    }

    private static string Flipped(string way) =>
        way == nameof(ParallelProbeWay.Left)
            ? nameof(ParallelProbeWay.Right)
            : nameof(ParallelProbeWay.Left);

    /// <summary>A manifest with no retries whose run failed in a branch and was dead-lettered.</summary>
    private async Task<(Manifest Manifest, Metadata Failed)> DeadLetteredAsync(string value)
    {
        var manifest = await CreateManifestAsync(value, maxRetries: 0);

        ParallelProbe.Fail = true;
        var failed = await CycleAsync(manifest);
        failed.TrainState.Should().Be(TrainState.Failed);
        await RunManifestManagerAsync();
        (await AwaitingDeadLetterOf(manifest)).Should().NotBeNull();

        ParallelProbe.Fail = false;
        return (manifest, failed);
    }

    [Test]
    public async Task A_parallel_run_past_its_job_timeout_is_recorded_cancelled()
    {
        var manifest = await CreateManifestAsync("timed-out", timeoutSeconds: 60);
        ParallelProbe.Hold = true;

        await RunManifestManagerAsync();
        var dispatching = Task.Run(() => DispatchQueuedAsync(manifest));
        await ParallelProbe.Held.WaitAsync(TimeSpan.FromSeconds(30));

        // The run has been going for longer than its manifest allows.
        var runId = await WithData(async data =>
        {
            var run = await data.Metadatas.SingleAsync(m =>
                m.ManifestId == manifest.Id && m.TrainState == TrainState.InProgress
            );
            await data
                .Metadatas.Where(m => m.Id == run.Id)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(m => m.StartTime, DateTime.UtcNow.AddMinutes(-5))
                );
            return run.Id;
        });

        await RunManifestManagerAsync();
        ParallelProbe.Release();
        var run = await dispatching.WaitAsync(TimeSpan.FromSeconds(30));

        run.Id.Should().Be(runId);
        run.TrainState.Should()
            .Be(TrainState.Cancelled, "the timeout asked the run to stop while its branches ran");
        run.CancellationRequested.Should().BeTrue();
    }

    private async Task<Manifest> CreateManifestAsync(
        string value,
        int maxRetries = 3,
        int? timeoutSeconds = null
    )
    {
        using var scope = Provider.CreateScope();
        var data = scope.ServiceProvider.GetRequiredService<IDataContext>();

        var group = await TestSetup.CreateAndSaveManifestGroup(
            data,
            name: $"group-{Guid.NewGuid():N}"
        );
        var manifest = Manifest.Create(
            new CreateManifest
            {
                Name = typeof(IParallelProbeTrain),
                IsEnabled = true,
                ScheduleType = ScheduleType.Interval,
                IntervalSeconds = 3600,
                MaxRetries = maxRetries,
                Properties = new ParallelProbeInput { Value = value },
            }
        );
        manifest.ManifestGroupId = group.Id;
        manifest.TimeoutSeconds = timeoutSeconds;
        await data.Track(manifest);
        await data.SaveChanges(CancellationToken.None);
        return manifest;
    }

    private async Task<Metadata> CycleAsync(Manifest manifest)
    {
        await RunManifestManagerAsync();
        return await DispatchQueuedAsync(manifest);
    }

    private async Task RunManifestManagerAsync()
    {
        using var scope = Provider.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IManifestManagerTrain>().Run(Unit.Default);
    }

    private async Task<Metadata> DispatchQueuedAsync(Manifest manifest)
    {
        var entryId = await WithData(data =>
            data.WorkQueues.AsNoTracking()
                .Where(q => q.ManifestId == manifest.Id && q.Status == WorkQueueStatus.Queued)
                .Select(q => q.Id)
                .SingleAsync()
        );

        using (var scope = Provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>().Run(Unit.Default);

        return await WithData(async data =>
        {
            var entry = await data.WorkQueues.AsNoTracking().SingleAsync(q => q.Id == entryId);
            entry.Status.Should().Be(WorkQueueStatus.Dispatched);
            return await data.Metadatas.AsNoTracking().SingleAsync(m => m.Id == entry.MetadataId);
        });
    }

    private Task<DeadLetter?> AwaitingDeadLetterOf(Manifest manifest) =>
        WithData(data =>
            data.DeadLetters.AsNoTracking()
                .SingleOrDefaultAsync(d =>
                    d.ManifestId == manifest.Id && d.Status == DeadLetterStatus.AwaitingIntervention
                )
        );

    private async Task<Metadata> RequeueDeadLetterAndRunAsync(Manifest manifest, bool askAfresh)
    {
        var deadLetter = await AwaitingDeadLetterOf(manifest);

        using (var scope = Provider.CreateScope())
            (
                await scope
                    .ServiceProvider.GetRequiredService<ITraxScheduler>()
                    .RequeueDeadLetterAsync(deadLetter!.Id, askAfresh)
            )
                .Success.Should()
                .BeTrue();

        return await DispatchQueuedAsync(manifest);
    }

    private Task<List<Effect.Models.RecordedDecision.RecordedDecision>> DecisionsOf(
        long metadataId
    ) =>
        WithData(data =>
            data.RecordedDecisions.AsNoTracking()
                .Where(d => d.MetadataId == metadataId)
                .ToListAsync()
        );

    private async Task<T> WithData<T>(Func<IDataContext, Task<T>> read)
    {
        using var scope = Provider.CreateScope();
        return await read(scope.ServiceProvider.GetRequiredService<IDataContext>());
    }
}

public record ParallelProbeInput : IManifestProperties
{
    public string Value { get; set; } = string.Empty;
}

[Asks("Which way does this branch go?")]
public enum ParallelProbeWay
{
    Left,
    Right,
}

public record ParallelProbeFast(string Value);

public record ParallelProbeSlow(string Value);

/// <summary>What the Parallel probe's runs did, shared because the scheduler builds the junctions.</summary>
public static class ParallelProbe
{
    private static TaskCompletionSource _held = New();
    private static TaskCompletionSource _released = New();

    public static ParallelProbeDecider Decider { get; } = new();

    /// <summary>Whether the slow branch fails its next run.</summary>
    public static bool Fail { get; set; }

    /// <summary>Whether the fast branch holds its next run until <see cref="Release"/>.</summary>
    public static bool Hold { get; set; }

    public static Task Held => _held.Task;

    public static ConcurrentQueue<(long Run, string Branch, string Way)> Taken { get; } = new();

    public static void Reset()
    {
        Fail = false;
        Hold = false;
        _held = New();
        _released = New();
        Taken.Clear();
        Decider.Reset();
    }

    public static void Release() => _released.TrySetResult();

    public static async Task HoldIfAsked()
    {
        if (!Hold)
            return;

        _held.TrySetResult();
        await _released.Task.WaitAsync(TimeSpan.FromSeconds(30));
    }

    public static List<(string Branch, string Way)> TakenBy(long run) =>
        Taken.Where(t => t.Run == run).Select(t => (t.Branch, t.Way)).ToList();

    private static TaskCompletionSource New() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>Answers Left in the fast branch and Right in the slow one, or the other way once flipped.</summary>
public sealed class ParallelProbeDecider : IDecider
{
    private volatile bool _flipped;
    private int _asked;

    public int Asked => _asked;

    public void Flip() => _flipped = !_flipped;

    public void Reset()
    {
        _flipped = false;
        _asked = 0;
    }

    public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _asked);
        var fast = ChainGraph.CurrentBranchPath == "Parallel#0/fast";
        var way = fast ^ _flipped ? ParallelProbeWay.Left : ParallelProbeWay.Right;
        return new ScriptedDecider().Choose(way).Decide(request, cancellationToken);
    }
}

public sealed class ParallelProbeClassifier : IFailureClassifier
{
    public FailureClass? Classify(Exception exception) =>
        exception is TimeoutException ? FailureClass.Transient : null;
}

public class ParallelProbeWent(string way) : EffectJunction<ParallelProbeInput, Unit>
{
    public override Task<Unit> Run(ParallelProbeInput input)
    {
        ParallelProbe.Taken.Enqueue(
            (Metadata!.TrainMetadataId, ChainGraph.CurrentBranchPath ?? "", way)
        );
        return Task.FromResult(Unit.Default);
    }
}

public class ParallelProbeWentLeft() : ParallelProbeWent(nameof(ParallelProbeWay.Left));

public class ParallelProbeWentRight() : ParallelProbeWent(nameof(ParallelProbeWay.Right));

public class ParallelProbeFastWork : EffectJunction<ParallelProbeInput, ParallelProbeFast>
{
    public override async Task<ParallelProbeFast> Run(ParallelProbeInput input)
    {
        await ParallelProbe.HoldIfAsked();
        return new ParallelProbeFast(input.Value);
    }
}

/// <summary>Runs after the held junction, so a cancel flag set while it was held is read here.</summary>
public class ParallelProbeFastDone : EffectJunction<ParallelProbeFast, ParallelProbeFast>
{
    public override Task<ParallelProbeFast> Run(ParallelProbeFast input) => Task.FromResult(input);
}

public class ParallelProbeSlowWork : EffectJunction<ParallelProbeInput, ParallelProbeSlow>
{
    public override async Task<ParallelProbeSlow> Run(ParallelProbeInput input)
    {
        if (ParallelProbe.Hold)
        {
            // Stopped by its run's cancellation or by the fast branch's.
            var stopped = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously
            );
            await using (CancellationToken.Register(() => stopped.TrySetCanceled()))
                await stopped.Task;
        }

        if (ParallelProbe.Fail)
            throw new TimeoutException("the slow branch timed out");

        return new ParallelProbeSlow(input.Value);
    }
}

public class ParallelProbeStart : EffectJunction<ParallelProbeInput, ParallelProbeInput>
{
    public override Task<ParallelProbeInput> Run(ParallelProbeInput input) =>
        Task.FromResult(input);
}

public class ParallelProbeJoin : EffectJunction<(ParallelProbeFast, ParallelProbeSlow), Unit>
{
    public override Task<Unit> Run((ParallelProbeFast, ParallelProbeSlow) input) =>
        Task.FromResult(Unit.Default);
}

public interface IParallelProbeTrain : IServiceTrain<ParallelProbeInput, Unit>;

/// <summary>Two branches, each asking the same question and then working; the slow one can fail.</summary>
public class ParallelProbeTrain : ServiceTrain<ParallelProbeInput, Unit>, IParallelProbeTrain
{
    protected override Task<Either<Exception, Unit>> Junctions() =>
        Chain<ParallelProbeStart>()
            .Parallel(p =>
                p.Branch(
                        "fast",
                        b =>
                            b.Switch<ParallelProbeInput, ParallelProbeWay>(Ways)
                                .Chain<ParallelProbeFastWork>()
                                .Chain<ParallelProbeFastDone>()
                    )
                    .Branch(
                        "slow",
                        b =>
                            b.Switch<ParallelProbeInput, ParallelProbeWay>(Ways)
                                .Chain<ParallelProbeSlowWork>()
                    )
            )
            .Chain<ParallelProbeJoin>()
            .Resolve();

    private static Tracks<ParallelProbeInput, Unit, ParallelProbeWay> Ways(
        Tracks<ParallelProbeInput, Unit, ParallelProbeWay> tracks
    ) =>
        tracks
            .When(ParallelProbeWay.Left, t => t.Chain<ParallelProbeWentLeft>())
            .When(ParallelProbeWay.Right, t => t.Chain<ParallelProbeWentRight>());
}
