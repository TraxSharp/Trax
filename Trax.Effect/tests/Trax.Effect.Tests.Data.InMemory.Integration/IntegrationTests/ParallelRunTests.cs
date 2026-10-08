using System.Collections.Concurrent;
using AwesomeAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
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
    private ServiceProvider _provider = null!;

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
    public async Task Dispose() => await _provider.DisposeAsync();

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
        train.Metadata!.TrainState.Should().Be(TrainState.Completed);

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

        var rows = await Rows(train.Metadata!.Id);
        var route = rows.Should().ContainSingle(r => r.Kind == JunctionRunKind.Route).Subject;
        route.BranchPath.Should().Be("Parallel#0/routes");

        rows.Single(r => r.Name == nameof(ParInspect))
            .TrackPosition.Should()
            .Be(route.Position, "it runs on the track its own branch took");

        // The plain branch's junctions run after the routing branch has taken its track.
        rows.Where(r => r.BranchPath == "Parallel#0/plain")
            .Should()
            .HaveCount(2)
            .And.OnlyContain(
                r => r.TrackPosition == null,
                "a sibling's routing step is not on this branch's path"
            );
        rows.Single(r => r.Name == nameof(ParSeal)).TrackPosition.Should().BeNull();
    }

    [Test]
    public async Task A_withheld_track_in_one_branch_withholds_its_own_steps_and_everything_after_the_join_but_not_its_siblings()
    {
        Decider.Script(always: ParLane.Ground);

        var (train, _) = await Run<IParSiblingCustomsTrain>();

        var rows = await Rows(train.Metadata!.Id);
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

        rows.MaxBy(r => r.Position)!
            .NameWithheld.Should()
            .BeTrue("the step after the join follows every branch, the withheld one too");
    }

    [Test]
    public async Task Branches_asking_one_question_each_record_their_own_asking_and_a_requeue_replays_each()
    {
        // Before the fork, in each branch, and after the join: four askings of one question.
        Decider.Script(
            byBranch: new()
            {
                [""] = [ParLane.Express, ParLane.Ground],
                ["Parallel#0/a"] = [ParLane.Ground],
                ["Parallel#0/b"] = [ParLane.Express],
            }
        );

        var (original, _) = await Run<IParAskEverywhereTrain>();

        var recorded = await Recorded(original.Metadata!.Id);
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
        ParTook.Of(requeued.Metadata!.Id).Should().BeEquivalentTo(taken);
        (await Recorded(requeued.Metadata.Id))
            .Should()
            .HaveCount(4)
            .And.OnlyContain(r => r.Replayed);
    }

    [Test]
    public async Task A_run_cancelled_by_its_caller_while_branches_run_is_recorded_cancelled()
    {
        using var cancel = new CancellationTokenSource();
        var scope = _provider.CreateScope();
        var train =
            (ServiceTrain<ParOrder, string>)
                (object)scope.ServiceProvider.GetRequiredService<IParWaitTrain>();

        var running = train.Run(new ParOrder("o"), cancel.Token);
        await ParMeeting.BothWaiting.WaitAsync(TimeSpan.FromSeconds(30));
        await cancel.CancelAsync();

        await FluentActions
            .Awaiting(() => running)
            .Should()
            .ThrowAsync<OperationCanceledException>();
        train.Metadata!.TrainState.Should().Be(TrainState.Cancelled);

        await _provider.GetRequiredService<JunctionRunWriter>().FlushAsync();
        (await Rows(train.Metadata.Id))
            .Where(r => r.Name is nameof(ParWaitA) or nameof(ParWaitB))
            .Should()
            .HaveCount(2)
            .And.OnlyContain(r => r.State == JunctionRunState.Cancelled);
        scope.Dispose();
    }

    [Test]
    public async Task A_failed_branch_fails_the_run_with_the_class_the_classifier_gives_each_branch()
    {
        ParFailures.A = () => new TimeoutException("a timed out");
        ParFailures.B = () => new TimeoutException("b timed out");

        var train = await Fail<IParFailTrain>();

        train.Metadata!.TrainState.Should().Be(TrainState.Failed);
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

        train.Metadata!.FailureClass.Should().Be(FailureClass.Permanent);
    }

    private async Task<(ServiceTrain<ParOrder, string> Train, string Output)> Run<TTrain>(
        long? replayDecisionsOf = null
    )
        where TTrain : class, IServiceTrain<ParOrder, string>
    {
        using var scope = _provider.CreateScope();
        var train =
            (ServiceTrain<ParOrder, string>)
                (object)scope.ServiceProvider.GetRequiredService<TTrain>();
        var input = new ParOrder("o");
        var metadata = Metadata.Create(
            new CreateMetadata
            {
                Name = typeof(TTrain).FullName!,
                ExternalId = Guid.NewGuid().ToString("N"),
                Input = input,
                ReplayDecisionsOf = replayDecisionsOf,
            }
        );

        var output = await train.Run(input, metadata);
        await _provider.GetRequiredService<JunctionRunWriter>().FlushAsync();
        return (train, output);
    }

    private async Task<ServiceTrain<ParOrder, string>> Fail<TTrain>()
        where TTrain : class, IServiceTrain<ParOrder, string>
    {
        using var scope = _provider.CreateScope();
        var train =
            (ServiceTrain<ParOrder, string>)
                (object)scope.ServiceProvider.GetRequiredService<TTrain>();

        await FluentActions
            .Awaiting(() => train.Run(new ParOrder("o")))
            .Should()
            .ThrowAsync<BranchesFailedException>();
        return train;
    }

    private async Task<List<JunctionRun>> Rows(long metadataId)
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IDataContext>();
        return await context.JunctionRuns.AsNoTracking().ForRun(metadataId).ToListAsync();
    }

    private async Task<List<RecordedDecision>> Recorded(long metadataId)
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<IDataContext>();
        return await context
            .RecordedDecisions.AsNoTracking()
            .Where(d => d.MetadataId == metadataId)
            .OrderBy(d => d.Id)
            .ToListAsync();
    }
}

public sealed record ParOrder(string Id);

public sealed record ParA(int Score);

public sealed record ParB(int Score);

public sealed record ParRefinedA(int Score);

public sealed record ParRefinedB(int Score);

public sealed record ParInspected(string Id);

public sealed record ParPacked(string Id);

[Asks("Which lane does this order take?")]
public enum ParLane
{
    Express,
    Ground,
}

[Trax.Effect.Attributes.TraxSensitive]
[Asks("Which customs tier applies to this order?")]
public enum ParTier
{
    Express,
    Ground,
}

/// <summary>
/// Makes two branches meet: each waits for the other to arrive, so a run that passes ran them side
/// by side. Also says when both waiting junctions are waiting, and when a branch has inspected.
/// </summary>
internal static class ParMeeting
{
    private static TaskCompletionSource _a = New();
    private static TaskCompletionSource _b = New();
    private static TaskCompletionSource _inspected = New();
    private static int _waiting;
    private static TaskCompletionSource _bothWaiting = New();

    public static bool Met { get; private set; }

    public static Task BothWaiting => _bothWaiting.Task;

    public static Task Inspected => _inspected.Task;

    public static void Reset()
    {
        (_a, _b, _inspected, _bothWaiting) = (New(), New(), New(), New());
        _waiting = 0;
        Met = false;
    }

    public static async Task Arrive(bool isA)
    {
        (isA ? _a : _b).TrySetResult();
        await (isA ? _b : _a).Task.WaitAsync(TimeSpan.FromSeconds(30));
        Met = true;
    }

    public static void Waiting()
    {
        if (Interlocked.Increment(ref _waiting) == 2)
            _bothWaiting.TrySetResult();
    }

    public static void InspectionDone() => _inspected.TrySetResult();

    /// <summary>Completes only when <paramref name="token"/> is cancelled, by throwing.</summary>
    public static async Task UntilCancelled(CancellationToken token)
    {
        var cancelled = New();
        await using (token.Register(() => cancelled.TrySetCanceled(token)))
            await cancelled.Task;
    }

    private static TaskCompletionSource New() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

/// <summary>Answers by the branch asking, in order, and counts what it was asked.</summary>
internal sealed class ParDecider : IDecider
{
    private Dictionary<string, Queue<ParLane>> _byBranch = [];
    private ParLane? _always;
    private int _asked;

    public int Asked => _asked;

    public void Script(Dictionary<string, ParLane[]>? byBranch = null, ParLane? always = null)
    {
        _byBranch = (byBranch ?? []).ToDictionary(b => b.Key, b => new Queue<ParLane>(b.Value));
        _always = always;
        _asked = 0;
    }

    public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _asked);

        var branch = ChainGraph.CurrentBranchPath ?? "";
        ParLane lane;
        lock (_byBranch)
            lane =
                _byBranch.TryGetValue(branch, out var queue) && queue.Count > 0
                    ? queue.Dequeue()
                    : _always ?? ParLane.Ground;

        return new ScriptedDecider()
            .Choose(lane)
            .Choose(lane == ParLane.Express ? ParTier.Express : ParTier.Ground)
            .Decide(request, cancellationToken);
    }
}

internal sealed class ParClassifier : IFailureClassifier
{
    public FailureClass? Classify(Exception exception) =>
        exception switch
        {
            TimeoutException => FailureClass.Transient,
            InvalidOperationException => FailureClass.Permanent,
            _ => null,
        };
}

internal static class ParFailures
{
    public static Func<Exception> A { get; set; } = () => new InvalidOperationException("a");
    public static Func<Exception> B { get; set; } = () => new InvalidOperationException("b");
}

/// <summary>Which track each run took, by branch.</summary>
internal static class ParTook
{
    private static readonly ConcurrentQueue<(long Run, string Branch, string Track)> Taken = new();

    public static void Note(long run, string track) =>
        Taken.Enqueue((run, ChainGraph.CurrentBranchPath ?? "", track));

    public static List<(string Branch, string Track)> Of(long run) =>
        Taken.Where(t => t.Run == run).Select(t => (t.Branch, t.Track)).ToList();
}

public class ParStart : EffectJunction<ParOrder, ParOrder>
{
    public override Task<ParOrder> Run(ParOrder input) => Task.FromResult(input);
}

public class ParScoreA : EffectJunction<ParOrder, ParA>
{
    public override async Task<ParA> Run(ParOrder input)
    {
        await ParMeeting.Arrive(isA: true);
        return new ParA(1);
    }
}

public class ParRefineA : EffectJunction<ParA, ParRefinedA>
{
    public override async Task<ParRefinedA> Run(ParA input)
    {
        await Task.Yield();
        return new ParRefinedA(input.Score * 2);
    }
}

public class ParScoreB : EffectJunction<ParOrder, ParB>
{
    public override async Task<ParB> Run(ParOrder input)
    {
        await ParMeeting.Arrive(isA: false);
        return new ParB(10);
    }
}

public class ParRefineB : EffectJunction<ParB, ParRefinedB>
{
    public override async Task<ParRefinedB> Run(ParB input)
    {
        await Task.Yield();
        return new ParRefinedB(input.Score * 2);
    }
}

public class ParCombine : EffectJunction<(ParRefinedA, ParRefinedB), string>
{
    public override Task<string> Run((ParRefinedA, ParRefinedB) input) =>
        Task.FromResult($"a={input.Item1.Score}, b={input.Item2.Score}");
}

public class ParInspect : EffectJunction<ParOrder, ParInspected>
{
    public override Task<ParInspected> Run(ParOrder input)
    {
        ParMeeting.InspectionDone();
        return Task.FromResult(new ParInspected(input.Id));
    }
}

/// <summary>Runs only once its sibling branch has taken its track and inspected.</summary>
public class ParAwaitInspection : EffectJunction<ParOrder, ParOrder>
{
    public override async Task<ParOrder> Run(ParOrder input)
    {
        await ParMeeting.Inspected.WaitAsync(TimeSpan.FromSeconds(30));
        return input;
    }
}

public class ParPack : EffectJunction<ParOrder, ParPacked>
{
    public override Task<ParPacked> Run(ParOrder input) => Task.FromResult(new ParPacked(input.Id));
}

public class ParSeal : EffectJunction<(ParInspected, ParPacked), string>
{
    public override Task<string> Run((ParInspected, ParPacked) input) => Task.FromResult("sealed");
}

public class ParTookExpress : EffectJunction<ParOrder, Unit>
{
    public override Task<Unit> Run(ParOrder input)
    {
        ParTook.Note(Metadata!.TrainMetadataId, nameof(ParLane.Express));
        return Task.FromResult(Unit.Default);
    }
}

public class ParTookGround : EffectJunction<ParOrder, Unit>
{
    public override Task<Unit> Run(ParOrder input)
    {
        ParTook.Note(Metadata!.TrainMetadataId, nameof(ParLane.Ground));
        return Task.FromResult(Unit.Default);
    }
}

public class ParFinish : EffectJunction<ParOrder, string>
{
    public override Task<string> Run(ParOrder input) => Task.FromResult("done");
}

/// <summary>Waits for its branch's token, which only a cancellation ends.</summary>
public class ParWaitA : EffectJunction<ParOrder, ParA>
{
    public override async Task<ParA> Run(ParOrder input)
    {
        ParMeeting.Waiting();
        await ParMeeting.UntilCancelled(CancellationToken);
        return new ParA(0);
    }
}

public class ParWaitB : EffectJunction<ParOrder, ParB>
{
    public override async Task<ParB> Run(ParOrder input)
    {
        ParMeeting.Waiting();
        await ParMeeting.UntilCancelled(CancellationToken);
        return new ParB(0);
    }
}

public class ParBreakA : EffectJunction<ParOrder, ParA>
{
    public override async Task<ParA> Run(ParOrder input)
    {
        await Task.Yield();
        throw ParFailures.A();
    }
}

public class ParBreakB : EffectJunction<ParOrder, ParB>
{
    public override async Task<ParB> Run(ParOrder input)
    {
        await Task.Yield();
        throw ParFailures.B();
    }
}

public interface IParSignalsTrain : IServiceTrain<ParOrder, string>;

public class ParSignalsTrain : ServiceTrain<ParOrder, string>, IParSignalsTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<ParStart>()
            .Parallel(p =>
                p.Branch("a", b => b.Chain<ParScoreA>().Chain<ParRefineA>())
                    .Branch("b", b => b.Chain<ParScoreB>().Chain<ParRefineB>())
            )
            .Chain<ParCombine>()
            .Resolve();
}

public interface IParSiblingTrackTrain : IServiceTrain<ParOrder, string>;

/// <summary>One branch routes, and its sibling runs only once that branch is on its track.</summary>
public class ParSiblingTrackTrain : ServiceTrain<ParOrder, string>, IParSiblingTrackTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<ParStart>()
            .Parallel(p =>
                p.Branch(
                        "routes",
                        b =>
                            b.Switch<ParOrder, ParLane>(t =>
                                t.When(ParLane.Express, x => x.Chain<ParInspect>())
                                    .When(ParLane.Ground, x => x.Chain<ParInspect>())
                            )
                    )
                    .Branch("plain", b => b.Chain<ParAwaitInspection>().Chain<ParPack>())
            )
            .Chain<ParSeal>()
            .Resolve();
}

public interface IParSiblingCustomsTrain : IServiceTrain<ParOrder, string>;

/// <summary>As <see cref="ParSiblingTrackTrain"/>, routing on a question marked [TraxSensitive].</summary>
public class ParSiblingCustomsTrain : ServiceTrain<ParOrder, string>, IParSiblingCustomsTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<ParStart>()
            .Parallel(p =>
                p.Branch(
                        "routes",
                        b =>
                            b.Switch<ParOrder, ParTier>(t =>
                                t.When(ParTier.Express, x => x.Chain<ParInspect>())
                                    .When(ParTier.Ground, x => x.Chain<ParInspect>())
                            )
                    )
                    .Branch("plain", b => b.Chain<ParAwaitInspection>().Chain<ParPack>())
            )
            .Chain<ParSeal>()
            .Resolve();
}

public interface IParAskEverywhereTrain : IServiceTrain<ParOrder, string>;

/// <summary>Asks one question before the fork, in each of two branches, and after the join.</summary>
public class ParAskEverywhereTrain : ServiceTrain<ParOrder, string>, IParAskEverywhereTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Switch<ParOrder, ParLane>(Lanes)
            .Parallel(p =>
                p.Branch("a", b => b.Switch<ParOrder, ParLane>(Lanes))
                    .Branch("b", b => b.Switch<ParOrder, ParLane>(Lanes))
            )
            .Switch<ParOrder, ParLane>(Lanes)
            .Chain<ParFinish>()
            .Resolve();

    private static Tracks<ParOrder, string, ParLane> Lanes(
        Tracks<ParOrder, string, ParLane> tracks
    ) =>
        tracks
            .When(ParLane.Express, t => t.Chain<ParTookExpress>())
            .When(ParLane.Ground, t => t.Chain<ParTookGround>());
}

public interface IParWaitTrain : IServiceTrain<ParOrder, string>;

public class ParWaitTrain : ServiceTrain<ParOrder, string>, IParWaitTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<ParStart>()
            .Parallel(p =>
                p.Branch("a", b => b.Chain<ParWaitA>().Chain<ParRefineA>())
                    .Branch("b", b => b.Chain<ParWaitB>().Chain<ParRefineB>())
            )
            .Chain<ParCombine>()
            .Resolve();
}

public interface IParFailTrain : IServiceTrain<ParOrder, string>;

/// <summary>Both branches fail, each as <see cref="ParFailures"/> says, and both are waited for.</summary>
public class ParFailTrain : ServiceTrain<ParOrder, string>, IParFailTrain
{
    protected override Task<Either<Exception, string>> Junctions() =>
        Chain<ParStart>()
            .Parallel(p =>
                p.Branch("a", b => b.Chain<ParBreakA>().Chain<ParRefineA>())
                    .Branch("b", b => b.Chain<ParBreakB>().Chain<ParRefineB>())
                    .OnFailure(BranchFailurePolicy.WaitForAll)
            )
            .Chain<ParCombine>()
            .Resolve();
}
