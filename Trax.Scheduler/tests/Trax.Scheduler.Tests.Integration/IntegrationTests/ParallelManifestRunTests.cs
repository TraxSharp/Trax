using System.Collections.Concurrent;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Extensions;
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
    private ServiceProvider? _provider;

    // A host built for one test, in place of the shared one. Disposed by that test.
    private IServiceProvider? _override;

    private IServiceProvider Provider => _override ?? _provider.NotNull();

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
    public async Task DisposeProvider()
    {
        if (_provider is not null)
            await _provider.DisposeAsync();
    }

    [SetUp]
    public async Task Clean()
    {
        ParallelProbe.Reset();

        var scope = Provider.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
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
        var delayed = BuildProvider(TimeSpan.FromMinutes(10));
        await using var delayedDisposal = delayed.ConfigureAwait(false);
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
                    TimeProvider.System.GetUtcNow().UtcDateTime.AddMinutes(9),
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
        string.Equals(way, nameof(ParallelProbeWay.Left), StringComparison.Ordinal)
            ? nameof(ParallelProbeWay.Right)
            : nameof(ParallelProbeWay.Left);

    /// <summary>A manifest with no retries whose run failed in a branch and was dead-lettered.</summary>
    private async Task<(Manifest Manifest, Metadata Failed)> DeadLetteredAsync(string value)
    {
        var manifest = await CreateManifestAsync(value, maxRetries: 0).ConfigureAwait(false);

        ParallelProbe.Fail = true;
        var failed = await CycleAsync(manifest).ConfigureAwait(false);
        failed.TrainState.Should().Be(TrainState.Failed);
        await RunManifestManagerAsync().ConfigureAwait(false);
        (await AwaitingDeadLetterOf(manifest).ConfigureAwait(false)).Should().NotBeNull();

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
        await ParallelProbe.Held.WaitAsync(TimeSpan.FromSeconds(30), TimeProvider.System);

        // The run has been going for longer than its manifest allows.
        var runId = await WithData(async data =>
        {
            var run = await data
                .Metadatas.SingleAsync(m =>
                    m.ManifestId == manifest.Id && m.TrainState == TrainState.InProgress
                )
                .ConfigureAwait(false);
            await data
                .Metadatas.Where(m => m.Id == run.Id)
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(
                        m => m.StartTime,
                        TimeProvider.System.GetUtcNow().UtcDateTime.AddMinutes(-5)
                    )
                )
                .ConfigureAwait(false);
            return run.Id;
        });

        await RunManifestManagerAsync();
        ParallelProbe.Release();
        var run = await dispatching.WaitAsync(TimeSpan.FromSeconds(30), TimeProvider.System);

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
        var scope = Provider.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var data = scope.ServiceProvider.GetRequiredService<IDataContext>();

        var group = await TestSetup
            .CreateAndSaveManifestGroup(data, name: $"group-{Guid.NewGuid():N}")
            .ConfigureAwait(false);
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
        await data.Track(manifest).ConfigureAwait(false);
        await data.SaveChanges(CancellationToken.None).ConfigureAwait(false);
        return manifest;
    }

    private async Task<Metadata> CycleAsync(Manifest manifest)
    {
        await RunManifestManagerAsync().ConfigureAwait(false);
        return await DispatchQueuedAsync(manifest).ConfigureAwait(false);
    }

    private async Task RunManifestManagerAsync()
    {
        var scope = Provider.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        await scope
            .ServiceProvider.GetRequiredService<IManifestManagerTrain>()
            .Run(Unit.Default)
            .ConfigureAwait(false);
    }

    private async Task<Metadata> DispatchQueuedAsync(Manifest manifest)
    {
        var entryId = await WithData(data =>
                data.WorkQueues.AsNoTracking()
                    .Where(q => q.ManifestId == manifest.Id && q.Status == WorkQueueStatus.Queued)
                    .Select(q => q.Id)
                    .SingleAsync()
            )
            .ConfigureAwait(false);

        var scope = Provider.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
            await scope
                .ServiceProvider.GetRequiredService<IJobDispatcherTrain>()
                .Run(Unit.Default)
                .ConfigureAwait(false);

        return await WithData(async data =>
            {
                var entry = await data
                    .WorkQueues.AsNoTracking()
                    .SingleAsync(q => q.Id == entryId)
                    .ConfigureAwait(false);
                entry.Status.Should().Be(WorkQueueStatus.Dispatched);
                return await data
                    .Metadatas.AsNoTracking()
                    .SingleAsync(m => m.Id == entry.MetadataId)
                    .ConfigureAwait(false);
            })
            .ConfigureAwait(false);
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
        var deadLetter = await AwaitingDeadLetterOf(manifest).ConfigureAwait(false);

        var scope = Provider.CreateAsyncScope();
        await using (scope.ConfigureAwait(false))
            (
                await scope
                    .ServiceProvider.GetRequiredService<ITraxScheduler>()
                    .RequeueDeadLetterAsync(deadLetter.NotNull().Id, askAfresh)
                    .ConfigureAwait(false)
            )
                .Success.Should()
                .BeTrue();

        return await DispatchQueuedAsync(manifest).ConfigureAwait(false);
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
        var scope = Provider.CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        return await read(scope.ServiceProvider.GetRequiredService<IDataContext>())
            .ConfigureAwait(false);
    }
}
