using System.Collections.Concurrent;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Extensions;
using Trax.Core.Functional;
using Trax.Core.Monad;
using Trax.Effect.Data.Extensions;
using Trax.Effect.Data.InMemory.Extensions;
using Trax.Effect.Data.JunctionEvents;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.JunctionRun;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.RecordedDecision;
using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.FailureClassifier;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Data.InMemory.Integration.IntegrationTests;

/// <summary>
/// A service train whose junctions run in <c>Parallel</c> branches: each branch's steps are
/// recorded under their own node and branch, a branch's tracks and withholding stay its own until
/// the join, two branches asking one question each record and replay their own answer, and a
/// cancelled or failed run is recorded as one run, with the class of every failed branch.
/// </summary>
[NonParallelizable]
public class ParallelRunTests
{
    private ServiceProvider? _provider;

    private static readonly ParDecider Decider = new();

    [OneTimeSetUp]
    public void Build() =>
        _provider = new ServiceCollection()
            .AddSingleton<IDecider>(Decider)
            .AddSingleton<IFailureClassifier, ParClassifier>()
            .AddScopedTraxRoute<IParSignalsTrain, ParSignalsTrain>()
            .AddScopedTraxRoute<IParSiblingTrackTrain, ParSiblingTrackTrain>()
            .AddScopedTraxRoute<IParSiblingCustomsTrain, ParSiblingCustomsTrain>()
            .AddScopedTraxRoute<IParAskEverywhereTrain, ParAskEverywhereTrain>()
            .AddScopedTraxRoute<IParWaitTrain, ParWaitTrain>()
            .AddScopedTraxRoute<IParFailTrain, ParFailTrain>()
            .AddTrax(trax =>
                trax.AddEffects(effects =>
                    effects.UseInMemory().AddJunctionEvents().AddDecisionRecording()
                )
            )
            .BuildServiceProvider();

    [OneTimeTearDown]
    public async Task Dispose() => await _provider.NotNull().DisposeAsync();

    [SetUp]
    public void Reset()
    {
        ParMeeting.Reset();
        Decider.Script();
        ParFailures.A = () => new InvalidOperationException("a broke");
        ParFailures.B = () => new InvalidOperationException("b broke");
    }

    [Test]
    public async Task Each_branch_junction_is_a_row_with_its_own_node_and_branch()
    {
        var (train, output) = await Run<IParSignalsTrain>();

        output.Should().Be("a=2, b=20");
        ParMeeting.Met.Should().BeTrue("the two branches' first junctions waited for each other");
        train.Metadata.NotNull().TrainState.Should().Be(TrainState.Completed);

        var rows = await Rows(train.Metadata.Id);
        rows.Should().OnlyContain(r => r.State == JunctionRunState.Completed);
        rows.Select(r => r.Position).Should().OnlyHaveUniqueItems();
        rows.Select(r => (r.Name, r.NodeId, r.BranchPath))
            .Should()
            .BeEquivalentTo([
                (nameof(ParStart), "ParStart#0", null),
                (nameof(ParScoreA), "Parallel#0/a/ParScoreA#0", "Parallel#0/a"),
                (nameof(ParRefineA), "Parallel#0/a/ParRefineA#0", "Parallel#0/a"),
                (nameof(ParScoreB), "Parallel#0/b/ParScoreB#0", "Parallel#0/b"),
                (nameof(ParRefineB), "Parallel#0/b/ParRefineB#0", "Parallel#0/b"),
                (nameof(ParCombine), "ParCombine#0", (string?)null),
            ]);
    }

    [Test]
    public async Task A_track_taken_in_one_branch_is_not_the_track_of_its_siblings_steps()
    {
        Decider.Script(always: ParLane.Ground);

        var (train, _) = await Run<IParSiblingTrackTrain>();

        var rows = await Rows(train.Metadata.NotNull().Id);
        var route = rows.Should().ContainSingle(r => r.Kind == JunctionRunKind.Route).Subject;
        route.BranchPath.Should().Be("Parallel#0/routes");

        rows.Single(r => string.Equals(r.Name, nameof(ParInspect), StringComparison.Ordinal))
            .TrackPosition.Should()
            .Be(route.Position, "it runs on the track its own branch took");

        // The plain branch's junctions run after the routing branch has taken its track.
        rows.Where(r => string.Equals(r.BranchPath, "Parallel#0/plain", StringComparison.Ordinal))
            .Should()
            .HaveCount(2)
            .And.OnlyContain(
                r => r.TrackPosition == null,
                "a sibling's routing step is not on this branch's path"
            );
        rows.Single(r => string.Equals(r.Name, nameof(ParSeal), StringComparison.Ordinal))
            .TrackPosition.Should()
            .BeNull();
    }

    [Test]
    public async Task A_withheld_track_in_one_branch_withholds_its_own_steps_and_everything_after_the_join_but_not_its_siblings()
    {
        Decider.Script(always: ParLane.Ground);

        var (train, _) = await Run<IParSiblingCustomsTrain>();

        var rows = await Rows(train.Metadata.NotNull().Id);
        var route = rows.Single(r => r.Kind == JunctionRunKind.Route);
        route.AnswerWithheld.Should().BeTrue();

        var inspected = rows.Single(r =>
            r.TrackPosition == route.Position && r.Position > route.Position
        );
        inspected.NameWithheld.Should().BeTrue();
        inspected.Name.Should().Be("(withheld)");
        inspected.NodeId.Should().BeNull();
        inspected.BranchPath.Should().BeNull("a branch path can name a track");

        rows.Where(r => r.Name is nameof(ParAwaitInspection) or nameof(ParPack))
            .Should()
            .HaveCount(2)
            .And.OnlyContain(r => !r.NameWithheld && r.BranchPath == "Parallel#0/plain");

        rows.MaxBy(r => r.Position)
            .NotNull()
            .NameWithheld.Should()
            .BeTrue("the step after the join follows every branch, the withheld one too");
    }

    [Test]
    public async Task Branches_asking_one_question_each_record_their_own_asking_and_a_requeue_replays_each()
    {
        // Before the fork, in each branch, and after the join: four askings of one question.
        Decider.Script(
            byBranch: new(StringComparer.Ordinal)
            {
                [""] = [ParLane.Express, ParLane.Ground],
                ["Parallel#0/a"] = [ParLane.Ground],
                ["Parallel#0/b"] = [ParLane.Express],
            }
        );

        var (original, _) = await Run<IParAskEverywhereTrain>();

        var recorded = await Recorded(original.Metadata.NotNull().Id);
        recorded
            .Select(r => (r.BranchPath, r.QuestionKey, r.Occurrence, r.Tracks().Single()))
            .Should()
            .BeEquivalentTo(
                [
                    ("", "ParLane", 0, "Express"),
                    ("Parallel#0/a", "ParLane", 1, "Ground"),
                    ("Parallel#0/b", "ParLane", 1, "Express"),
                    ("", "ParLane", 1, "Ground"),
                ],
                "the branches count on from the fork, so their askings are told apart by branch"
            );
        var taken = ParTook.Of(original.Metadata.Id);

        // Asked now, the decider would answer Express everywhere. The requeue must not ask.
        Decider.Script(always: ParLane.Express);
        var (requeued, _) = await Run<IParAskEverywhereTrain>(
            replayDecisionsOf: original.Metadata.Id
        );

        Decider.Asked.Should().Be(0);
        ParTook.Of(requeued.Metadata.NotNull().Id).Should().BeEquivalentTo(taken);
        (await Recorded(requeued.Metadata.Id))
            .Should()
            .HaveCount(4)
            .And.OnlyContain(r => r.Replayed);
    }

    [Test]
    public async Task A_run_cancelled_by_its_caller_while_branches_run_is_recorded_cancelled()
    {
        using var cancel = new CancellationTokenSource();
        var scope = _provider.NotNull().CreateAsyncScope();
        var train =
            (ServiceTrain<ParOrder, string>)
                (object)scope.ServiceProvider.GetRequiredService<IParWaitTrain>();

        var running = train.Run(new ParOrder("o"), cancel.Token);
        await ParMeeting.BothWaiting.WaitAsync(TimeSpan.FromSeconds(30), CancellationToken.None);
        await cancel.CancelAsync();

        await FluentActions
            .Awaiting(() => running)
            .Should()
            .ThrowAsync<OperationCanceledException>();
        train.Metadata.NotNull().TrainState.Should().Be(TrainState.Cancelled);

        await _provider
            .NotNull()
            .GetRequiredService<JunctionRunWriter>()
            .FlushAsync(CancellationToken.None);
        (await Rows(train.Metadata.Id))
            .Where(r => r.Name is nameof(ParWaitA) or nameof(ParWaitB))
            .Should()
            .HaveCount(2)
            .And.OnlyContain(r => r.State == JunctionRunState.Cancelled);
        await scope.DisposeAsync();
    }

    [Test]
    public async Task A_failed_branch_fails_the_run_with_the_class_the_classifier_gives_each_branch()
    {
        ParFailures.A = () => new TimeoutException("a timed out");
        ParFailures.B = () => new TimeoutException("b timed out");

        var train = await Fail<IParFailTrain>();

        train.Metadata.NotNull().TrainState.Should().Be(TrainState.Failed);
        train
            .Metadata.FailureClass.Should()
            .Be(FailureClass.Transient, "the classifier calls each branch's failure transient");
        train.Metadata.FailureException.Should().Be(nameof(BranchesFailedException));
        train.Metadata.FailureJunction.Should().MatchRegex("^Parallel#0/[ab]:ParBreak[AB]$");
    }

    [Test]
    public async Task One_permanent_branch_failure_makes_the_runs_failure_permanent()
    {
        ParFailures.A = () => new TimeoutException("a timed out");
        ParFailures.B = () => new InvalidOperationException("b broke");

        var train = await Fail<IParFailTrain>();

        train.Metadata.NotNull().FailureClass.Should().Be(FailureClass.Permanent);
    }

    private async Task<(ServiceTrain<ParOrder, string> Train, string Output)> Run<TTrain>(
        long? replayDecisionsOf = null
    )
        where TTrain : class, IServiceTrain<ParOrder, string>
    {
        var scope = _provider.NotNull().CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var train =
            (ServiceTrain<ParOrder, string>)
                (object)scope.ServiceProvider.GetRequiredService<TTrain>();
        var input = new ParOrder("o");
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(TTrain).FullName.NotNull(),
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = input,
                ReplayDecisionsOf = replayDecisionsOf,
            }
        );

        var output = await train.Run(input, metadata, CancellationToken.None).ConfigureAwait(false);
        await _provider
            .NotNull()
            .GetRequiredService<JunctionRunWriter>()
            .FlushAsync(CancellationToken.None)
            .ConfigureAwait(false);
        return (train, output);
    }

    private async Task<ServiceTrain<ParOrder, string>> Fail<TTrain>()
        where TTrain : class, IServiceTrain<ParOrder, string>
    {
        var scope = _provider.NotNull().CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var train =
            (ServiceTrain<ParOrder, string>)
                (object)scope.ServiceProvider.GetRequiredService<TTrain>();

        await FluentActions
            .Awaiting(() => train.Run(new ParOrder("o"), CancellationToken.None))
            .Should()
            .ThrowAsync<BranchesFailedException>()
            .ConfigureAwait(false);
        return train;
    }

    private async Task<List<JunctionRun>> Rows(long metadataId)
    {
        var scope = _provider.NotNull().CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var context = scope.ServiceProvider.GetRequiredService<IDataContext>();
        return await context
            .JunctionRuns.AsNoTracking()
            .ForRun(metadataId)
            .ToListAsync()
            .ConfigureAwait(false);
    }

    private async Task<List<RecordedDecision>> Recorded(long metadataId)
    {
        var scope = _provider.NotNull().CreateAsyncScope();
        await using var scopeDisposal = scope.ConfigureAwait(false);
        var context = scope.ServiceProvider.GetRequiredService<IDataContext>();
        return await context
            .RecordedDecisions.AsNoTracking()
            .Where(d => d.MetadataId == metadataId)
            .OrderBy(d => d.Id)
            .ToListAsync()
            .ConfigureAwait(false);
    }
}
