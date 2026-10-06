using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.Postgres.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Provider.Json.Extensions;
using Trax.Effect.Provider.Parameter.Extensions;
using Trax.Mediator.Extensions;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Tests.Integration.Fakes.Trains;
using Trax.Scheduler.Tests.Integration.Fixtures;
using Trax.Scheduler.Trains.JobDispatcher;
using Trax.Scheduler.Trains.JobRunner;

namespace Trax.Scheduler.Tests.Integration.IntegrationTests;

/// <summary>
/// A requeue end to end, on a host that records decisions: the run is queued and dispatched, it
/// asks the decider, it is requeued through <see cref="IOperationsService.RequeueExecutionAsync"/>,
/// and the requeued run is dispatched and run. It takes the tracks the run it repeats took
/// without asking the decider, which by then answers differently (central <c>docs/0041</c>).
/// </summary>
[TestFixture]
[NonParallelizable]
public class RequeueReplayEndToEndTests
{
    private ServiceProvider _provider = null!;
    private ScriptedDecider _decider = null!;

    [OneTimeSetUp]
    public void BuildProvider()
    {
        _decider = new ScriptedDecider();

        _provider = new ServiceCollection()
            .AddLogging(x => x.SetMinimumLevel(LogLevel.Warning))
            .AddSingleton<IDecider>(_decider)
            .AddTrax(trax =>
                trax.AddEffects(effects =>
                        effects
                            .SaveTrainParameters()
                            .UsePostgres(TestPostgres.ConnectionString)
                            .AddDecisionRecording()
                            .AddJson()
                    )
                    .AddMediator(typeof(AssemblyMarker).Assembly, typeof(JobRunnerTrain).Assembly)
                    .AddScheduler(scheduler => scheduler.UseInMemoryWorkers())
            )
            .AddScoped<IDataContext>(sp =>
                (IDataContext)sp.GetRequiredService<IDataContextProviderFactory>().Create()
            )
            .BuildServiceProvider();
    }

    [OneTimeTearDown]
    public async Task DisposeProvider() => await _provider.DisposeAsync();

    [SetUp]
    public async Task Clean()
    {
        DecisionProbe.Reset();
        Answer(ProbeLane.Slow, ProbeSize.Large);

        using var scope = _provider.CreateScope();
        await TestSetup.CleanupDatabase(scope.ServiceProvider.GetRequiredService<IDataContext>());
    }

    [TearDown]
    public void ResetProbe() => DecisionProbe.Reset();

    [Test]
    public async Task A_requeued_run_takes_the_tracks_the_run_it_repeats_took_without_asking()
    {
        var before = _decider.Requests.Count;
        var original = await RunFreshAsync("repeat");
        DecisionProbe.TracksOf("repeat").Should().Equal("Slow", "Large");
        var asked = _decider.Requests.Count;
        (asked - before).Should().Be(2, "the original asked both questions");

        // A decider asked now would send the run down the other tracks.
        Answer(ProbeLane.Fast, ProbeSize.Small);
        var requeued = await RequeueAndRunAsync(original);

        requeued.TrainState.Should().Be(TrainState.Completed, requeued.FailureReason);
        requeued.ReplayDecisionsOf.Should().Be(original);
        DecisionProbe
            .TracksOf("repeat")
            .Should()
            .Equal(["Slow", "Large", "Slow", "Large"], "the requeue repeats the original's tracks");
        _decider.Requests.Should().HaveCount(asked, "a replayed decision asks nothing");
        (await DecisionsOf(requeued.Id))
            .Should()
            .HaveCount(2)
            .And.OnlyContain(d => d.Replayed, "both answers came from the original");
    }

    [Test]
    public async Task A_requeue_of_a_requeue_that_recorded_nothing_replays_the_first_runs_answers()
    {
        var first = await RunFreshAsync("chain");

        DecisionProbe.FailAt = ProbeFailure.BeforeFirstQuestion;
        var second = await RequeueAndRunAsync(first);
        second.TrainState.Should().Be(TrainState.Failed);
        (await DecisionsOf(second.Id))
            .Should()
            .BeEmpty("the second run failed before its first question");

        DecisionProbe.FailAt = ProbeFailure.None;
        Answer(ProbeLane.Fast, ProbeSize.Small);
        var asked = _decider.Requests.Count;
        var third = await RequeueAndRunAsync(second.Id);

        third.TrainState.Should().Be(TrainState.Completed, third.FailureReason);
        third
            .ReplayDecisionsOf.Should()
            .Be(second.Id, "the second run replayed the first, so it has decisions to replay");
        DecisionProbe
            .TracksOf("chain")
            .Should()
            .Equal(["Slow", "Large", "Slow", "Large"], "the third run repeats the first's tracks");
        _decider.Requests.Should().HaveCount(asked, "nothing was asked afresh");
    }

    [Test]
    public async Task A_requeue_of_a_requeue_that_recorded_part_replays_both_runs_answers()
    {
        var first = await RunFreshAsync("partial");

        DecisionProbe.FailAt = ProbeFailure.BetweenQuestions;
        var second = await RequeueAndRunAsync(first);
        second.TrainState.Should().Be(TrainState.Failed);
        (await DecisionsOf(second.Id))
            .Select(d => d.QuestionKey)
            .Should()
            .Equal(
                [QuestionKey.For<ProbeLane>()],
                "the second run failed after its first question"
            );

        DecisionProbe.FailAt = ProbeFailure.None;
        Answer(ProbeLane.Fast, ProbeSize.Small);
        var asked = _decider.Requests.Count;
        var third = await RequeueAndRunAsync(second.Id);

        third.TrainState.Should().Be(TrainState.Completed, third.FailureReason);
        DecisionProbe
            .TracksOf("partial")
            .Should()
            .Equal(
                ["Slow", "Large", "Slow", "Slow", "Large"],
                "the lane comes from the second run and the size from the first"
            );
        _decider.Requests.Should().HaveCount(asked, "nothing was asked afresh");
    }

    private void Answer(ProbeLane lane, ProbeSize size) => _decider.Choose(lane).Choose(size);

    /// <summary>Queues the probe train with a fresh input, dispatches it, and returns its run's id.</summary>
    private async Task<long> RunFreshAsync(string value)
    {
        long entryId;

        using (var scope = _provider.CreateScope())
        {
            var queued = await scope
                .ServiceProvider.GetRequiredService<IOperationsService>()
                .QueueTrainAsync(
                    new QueueTrainInput(
                        typeof(IDecisionProbeTrain).FullName!,
                        $$"""{"Value":"{{value}}"}"""
                    ),
                    CancellationToken.None
                );
            queued.Success.Should().BeTrue(queued.Message);
            entryId = queued.Id!.Value;
        }

        var run = await DispatchAsync(entryId);
        run.TrainState.Should().Be(TrainState.Completed, run.FailureReason);
        return run.Id;
    }

    /// <summary>Requeues a run through the shared requeue, dispatches the entry, and returns its run.</summary>
    private async Task<Metadata> RequeueAndRunAsync(long metadataId)
    {
        long entryId;

        using (var scope = _provider.CreateScope())
        {
            var requeued = await scope
                .ServiceProvider.GetRequiredService<IOperationsService>()
                .RequeueExecutionAsync(metadataId, CancellationToken.None);
            requeued.Success.Should().BeTrue(requeued.Message);
            entryId = requeued.Id!.Value;
        }

        return await DispatchAsync(entryId);
    }

    private async Task<Metadata> DispatchAsync(long entryId)
    {
        using (var scope = _provider.CreateScope())
            await scope.ServiceProvider.GetRequiredService<IJobDispatcherTrain>().Run(Unit.Default);

        using var read = _provider.CreateScope();
        var data = read.ServiceProvider.GetRequiredService<IDataContext>();
        var entry = await data.WorkQueues.AsNoTracking().SingleAsync(q => q.Id == entryId);
        entry.Status.Should().Be(WorkQueueStatus.Dispatched);

        return await data.Metadatas.AsNoTracking().SingleAsync(m => m.Id == entry.MetadataId);
    }

    private async Task<List<Effect.Models.RecordedDecision.RecordedDecision>> DecisionsOf(
        long metadataId
    )
    {
        using var scope = _provider.CreateScope();

        return await scope
            .ServiceProvider.GetRequiredService<IDataContext>()
            .RecordedDecisions.AsNoTracking()
            .Where(d => d.MetadataId == metadataId)
            .OrderBy(d => d.Id)
            .ToListAsync();
    }
}
