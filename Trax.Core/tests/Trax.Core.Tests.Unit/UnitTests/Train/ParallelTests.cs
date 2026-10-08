using AwesomeAssertions;
using CsCheck;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Monad;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

/// <summary>
/// <c>Parallel</c>: a fixed set of named branches that run side by side within one run, each on
/// its own copy of Memory, its own scope and its own token, joined before the next step.
///
/// <para>Enforces Trax.Docs/adr/0045-a-parallel-step-runs-fixed-branches-on-copies-of-memory-and-the-join-commits.md.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0045-a-parallel-step-runs-fixed-branches-on-copies-of-memory-and-the-join-commits.md"
)]
public class ParallelTests : TestSetup
{
    private const string Adr =
        "Trax.Docs/adr/0045-a-parallel-step-runs-fixed-branches-on-copies-of-memory-and-the-join-commits.md";

    /// <summary>Long enough never to be reached by a run that is working, so it only names a hang.</summary>
    private static readonly TimeSpan Hang = TimeSpan.FromSeconds(30);

    #region Running

    [Test]
    public async Task Parallel_RunsEveryBranch_AndTheJoinSeesWhatEachAdded()
    {
        var result = await new SignalsTrain().RunEither("abc");

        result.IsRight.Should().BeTrue();
        result
            .ValueUnsafe()
            .Should()
            .Be("embedding=3, cocitation=6", $"the join reads every branch's output ({Adr})");
    }

    [Test]
    public async Task Branches_RunAtTheSameTime()
    {
        // Each branch waits for the other to start. Run one after the other, neither could finish.
        var meeting = new Meeting();

        var result = await new MeetingTrain(meeting).RunEither("x").WaitAsync(Hang);

        result.IsRight.Should().BeTrue($"branches start at once on the thread pool ({Adr})");
    }

    [Test]
    public async Task ABranch_SeesMemoryAsItWasAtTheFork_NotWhatASiblingAdds()
    {
        // "reads" runs only after "produces" has put an Embedding in its Memory, and still finds
        // none: each branch has its own copy, taken at the fork.
        var result = await new IsolationTrain().RunEither("abc").WaitAsync(Hang);

        result
            .Swap()
            .ValueUnsafe()
            .Should()
            .BeOfType<BranchesFailedException>()
            .Which.Failures.Should()
            .ContainSingle()
            .Which.Should()
            .Match<FailedBranch>(
                f => f.Branch == "Parallel#0/reads" && f.Exception.Message.Contains("Embedding"),
                $"a branch runs on its own copy of Memory taken at the fork ({Adr})"
            );
    }

    [Test]
    public async Task OneBranch_GivesWhatTheSameChainGivesWithoutParallel()
    {
        var plain = await new PlainTrain().RunEither("abcd");
        var parallel = await new OneBranchTrain().RunEither("abcd");

        parallel.ValueUnsafe().Should().Be(plain.ValueUnsafe(), "Parallel(a) is a");
    }

    [Test]
    public async Task Parallel_NestsInsideABranch_AndInsideATrack()
    {
        var capture = new Capture();

        var result = await new NestedTrain(capture).RunEither("abc").WaitAsync(Hang);

        result.IsRight.Should().BeTrue();
        capture
            .Steps.Should()
            .BeEquivalentTo([
                (
                    "Switch<Lane>#0/Left/Parallel#0/outer/Parallel#0/inner/Note#0",
                    "Switch<Lane>#0/Left/Parallel#0/outer/Parallel#0/inner"
                ),
                (
                    "Switch<Lane>#0/Left/Parallel#0/other/Note#0",
                    "Switch<Lane>#0/Left/Parallel#0/other"
                ),
            ]);
    }

    #endregion

    #region Ids

    [Test]
    public async Task ABranchsSteps_ReportTheirNodeAndBranch_AsTheGraphDrawsThem()
    {
        var capture = new Capture();
        var train = new DecidingBranchTrain(capture);
        var graph = ChainGraph.From(
            new DecidingBranchTrain(new Capture()).DeclaredChain(),
            typeof(DecidingBranchTrain),
            typeof(string),
            typeof(string)
        );

        var result = await train.RunEither("abc").WaitAsync(Hang);

        result.IsRight.Should().BeTrue();
        var parallel = graph.Nodes.Single(n => n.Kind == ChainStepKind.Parallel);
        parallel.Id.Should().Be("Parallel#0");
        parallel.Tracks.Select(t => t.Name).Should().Equal("decides", "plain");

        capture
            .Steps.Should()
            .BeEquivalentTo([
                ("Parallel#0/decides/Decide<ChoiceDecision<Lane>>#0", "Parallel#0/decides"),
                ("Parallel#0/decides/Switch<Lane>#0", "Parallel#0/decides"),
                ("Parallel#0/decides/Switch<Lane>#0/Left/Note#0", "Parallel#0/decides"),
                ("Parallel#0/plain/Note#0", "Parallel#0/plain"),
            ]);
        capture.Steps.Select(s => s.Node).Should().OnlyContain(id => Ids(graph.Nodes).Contains(id));
    }

    #endregion

    #region Failure and cancellation

    [Test]
    public async Task AFailingBranch_FailsTheStep_WithEveryBranchThatFailed_AndTheJoinNeverRuns()
    {
        var joined = new Seen();

        var result = await new FailingTrain(BranchFailurePolicy.WaitForAll, joined)
            .RunEither("abc")
            .WaitAsync(Hang);

        result.IsLeft.Should().BeTrue();
        var failure = result
            .Swap()
            .ValueUnsafe()
            .Should()
            .BeOfType<BranchesFailedException>()
            .Subject;
        failure.Step.Should().Be("Parallel#0");
        failure
            .Failures.Select(f => f.Branch)
            .Should()
            .BeEquivalentTo(["Parallel#0/permanent", "Parallel#0/transient"]);
        failure.CancelledBySibling.Should().BeEmpty();
        joined.Joined.Should().BeFalse("a failed step stops the chain");

        var data = (TrainExceptionData)failure.Data["TrainExceptionData"]!;
        data.Type.Should().Be(nameof(BranchesFailedException));
        data.FailureClass.Should()
            .Be(FailureClass.Permanent, $"one permanent failure makes the step permanent ({Adr})");
        data.Junction.Should().StartWith("Parallel#0/");
    }

    [Test]
    public async Task CancelSiblings_StopsASiblingInsideItsJunction_AndDoesNotCountItAsAFailure()
    {
        var waiter = new Waiter();

        var result = await new CancelSiblingsTrain(waiter).RunEither("abc").WaitAsync(Hang);

        await waiter.Cancelled.Task.WaitAsync(Hang);
        var failure = result
            .Swap()
            .ValueUnsafe()
            .Should()
            .BeOfType<BranchesFailedException>()
            .Subject;
        failure.Failures.Select(f => f.Branch).Should().Equal("Parallel#0/fails");
        failure
            .CancelledBySibling.Should()
            .Equal(
                ["Parallel#0/waits"],
                $"a sibling stopped by the failure is not itself a failure ({Adr})"
            );
    }

    [Test]
    public async Task CancellingTheRun_DuringAParallel_FailsWithTheCancellation()
    {
        using var cts = new CancellationTokenSource();
        var waiter = new Waiter();

        var run = new BothWaitTrain(waiter).Run("abc", cts.Token);
        await waiter.Entered.Task.WaitAsync(Hang);
        await cts.CancelAsync();
        var act = async () => await run.WaitAsync(Hang);

        await act.Should()
            .ThrowAsync<OperationCanceledException>(
                $"a cancelled run is recorded as cancelled, never as failed branches ({Adr})"
            );
    }

    [Test]
    public void Combine_IsAJoin_WherePermanentWinsAndTransientNeedsEveryPart()
    {
        BranchesFailedException
            .Combine([FailureClass.Transient, FailureClass.Transient])
            .Should()
            .Be(FailureClass.Transient);
        BranchesFailedException
            .Combine([FailureClass.Transient, null])
            .Should()
            .Be(FailureClass.Unclassified, "a failure nobody classified is not claimed transient");
        BranchesFailedException
            .Combine([FailureClass.Conflict, FailureClass.Transient])
            .Should()
            .Be(FailureClass.Conflict);
        BranchesFailedException
            .Combine([FailureClass.Unclassified, FailureClass.Permanent, FailureClass.Transient])
            .Should()
            .Be(FailureClass.Permanent);
    }

    [Test]
    public void Combine_IsAssociativeCommutativeAndIdempotent()
    {
        var classes = Gen.OneOfConst<FailureClass?>(
            null,
            FailureClass.Unclassified,
            FailureClass.Transient,
            FailureClass.Conflict,
            FailureClass.Permanent
        );

        Gen.Select(classes, classes, classes)
            .Sample(
                (a, b, c) =>
                    BranchesFailedException.Combine([a, BranchesFailedException.Combine([b, c])])
                        == BranchesFailedException.Combine([
                            BranchesFailedException.Combine([a, b]),
                            c,
                        ])
                    && BranchesFailedException.Combine([a, b])
                        == BranchesFailedException.Combine([b, a])
                    && BranchesFailedException.Combine([a, a])
                        == BranchesFailedException.Combine([a])
            );
    }

    #endregion

    #region Scopes

    [Test]
    public async Task EachBranch_GetsItsOwnScope_KeptUntilTheRunEnds_AndInitialized()
    {
        var services = new ServiceCollection();
        services.AddScoped<Scoped>();
        services.AddSingleton<IBranchScopeInitializer, TenantCopier>();
        services.AddScoped<Tenant>();
        await using var root = services.BuildServiceProvider(validateScopes: true);
        await using var runScope = root.CreateAsyncScope();
        runScope.ServiceProvider.GetRequiredService<Tenant>().Name = "acme";

        var train = new ScopedTrain(runScope.ServiceProvider);
        var result = await train.RunEither("abc").WaitAsync(Hang);

        result.IsRight.Should().BeTrue();
        var report = result.ValueUnsafe();
        report.Left.Should().NotBeSameAs(report.Right, $"each branch has its own scope ({Adr})");
        report.Left.Tenant.Should().Be("acme", "an initializer copies what the run's scope holds");
        report
            .LeftUsableAtTheJoin.Should()
            .BeTrue("a branch's scope lives until the run ends, not until the join");
        report.Left.Disposed.Should().BeTrue("the run disposes its branches' scopes when it ends");
    }

    [Test]
    public async Task ABranchWhoseScopeCannotBePrepared_Fails()
    {
        var services = new ServiceCollection();
        services.AddScoped<Scoped>();
        services.AddScoped<Tenant>();
        services.AddSingleton<IBranchScopeInitializer, ThrowingInitializer>();
        await using var root = services.BuildServiceProvider();

        var result = await new ScopedTrain(root).RunEither("abc").WaitAsync(Hang);

        result
            .Swap()
            .ValueUnsafe()
            .Should()
            .BeOfType<BranchesFailedException>()
            .Which.Failures.Should()
            .OnlyContain(f => f.Exception.Message.Contains("could not prepare its scope"));
    }

    #endregion

    #region Verification

    [Test]
    public void Verification_Passes_ForBranchesThatAddDifferentTypes()
    {
        Faults(new SignalsTrain()).Should().BeEmpty();
    }

    [Test]
    public void Verification_RefusesTwoBranchesProducingOneType()
    {
        Faults(new CollidingTrain())
            .Should()
            .ContainSingle()
            .Which.Reason.Should()
            .Contain("both produce 'System.Int32'");
    }

    [Test]
    public void Verification_RefusesABranchReplacingAValueFromBeforeTheFork()
    {
        Faults(new OverwritingTrain())
            .Should()
            .Contain(f => f.Reason.Contains("was in Memory before the Parallel"));
    }

    [Test]
    public void Verification_RefusesABranchReadingWhatOnlyASiblingProduces_EvenWhenTheContainerHasIt()
    {
        var faults = ChainVerification.Verify(
            new SiblingReadingTrain().DeclaredChain(),
            typeof(string),
            typeof(string),
            availableElsewhere: _ => true
        );

        faults.Should().Contain(f => f.Reason.Contains("which only branch 'produces' produces"));
    }

    [Test]
    public void Declaration_RefusesAShortCircuitInsideABranch()
    {
        new ShortCircuitInBranchTrain()
            .DeclaredChain()
            .Refusals.Should()
            .ContainSingle(r => r.Contains("inside a Parallel branch"));
    }

    [Test]
    public void Declaration_RefusesACallOnTheTrainInsideABranch()
    {
        new RootCallInBranchTrain()
            .DeclaredChain()
            .Refusals.Should()
            .Contain(r => r.Contains("calls Chain on the train itself"));
    }

    [Test]
    public void Declaration_RefusesOneJunctionInstanceInTwoBranches()
    {
        new SharedInstanceTrain()
            .DeclaredChain()
            .Refusals.Should()
            .ContainSingle(r => r.Contains("same Echo instance"));
    }

    [Test]
    public async Task TheSameSwitchEnum_InTwoBranches_RoutesEachBranchOnItsOwn()
    {
        var capture = new Capture();

        var result = await new TwoSwitchesTrain(capture).RunEither("abc").WaitAsync(Hang);

        result.IsRight.Should().BeTrue();
        capture
            .Steps.Select(s => s.Node)
            .Should()
            .BeEquivalentTo(
                [
                    "Parallel#0/a/Switch<Lane>#0/Left/Note#0",
                    "Parallel#0/b/Switch<Lane>#0/Left/Note#0",
                ],
                "which track a branch took stays the branch's"
            );
        Faults(new TwoSwitchesTrain(new Capture())).Select(f => f.Reason).Should().BeEmpty();
    }

    [Test]
    public async Task ATupleTheJoinNeeds_IsAssembledFromWhatTheBranchesProduced()
    {
        var result = await new SignalsTrain().RunEither("ab").WaitAsync(Hang);

        result.ValueUnsafe().Should().Be("embedding=2, cocitation=4");
    }

    [Test]
    public async Task AValueHandedToAddServicesBeforeTheFork_IsSeenInEveryBranch()
    {
        var shared = new Shared("before");

        var result = await new SharedServiceTrain(shared).RunEither("x").WaitAsync(Hang);

        result.ValueUnsafe().Should().Be("before|before");
    }

    [TestCase("", "with no name")]
    [TestCase("a/b", "cannot contain")]
    [TestCase("twice", "twice")]
    public void Declaration_RefusesABadBranchName(string name, string expected)
    {
        new NamedTrain(name).DeclaredChain().Refusals.Should().Contain(r => r.Contains(expected));
    }

    #endregion

    #region Helpers

    private static IReadOnlyList<ChainFault> Faults<TIn, TOut>(Train<TIn, TOut> train) =>
        ChainVerification.Verify(train.DeclaredChain(), typeof(TIn), typeof(TOut));

    private static HashSet<string> Ids(IEnumerable<ChainGraphNode> nodes) =>
        nodes.SelectMany(n => Ids(n.Tracks.SelectMany(t => t.Nodes)).Prepend(n.Id)).ToHashSet();

    [Asks("Which lane?")]
    public enum Lane
    {
        Left,
        Right,
    }

    public sealed record Embedding(int Score);

    public sealed record CoCitation(int Score);

    private sealed class Capture
    {
        public List<(string Node, string? Branch)> Steps { get; } = [];

        public void Note()
        {
            lock (Steps)
                Steps.Add((ChainGraph.CurrentNodeId!, ChainGraph.CurrentBranchPath));
        }
    }

    private sealed class Note(Capture capture) : Junction<string, Functional.Unit>
    {
        public override Task<Functional.Unit> Run(string input)
        {
            capture.Note();
            return Task.FromResult(Functional.Unit.Default);
        }
    }

    private sealed class ScoreEmbedding : Junction<string, Embedding>
    {
        public override Task<Embedding> Run(string input) =>
            Task.FromResult(new Embedding(input.Length));
    }

    private sealed class ScoreCoCitation : Junction<string, CoCitation>
    {
        public override Task<CoCitation> Run(string input) =>
            Task.FromResult(new CoCitation(input.Length * 2));
    }

    private sealed class Combine : Junction<(Embedding, CoCitation), string>
    {
        public override Task<string> Run((Embedding, CoCitation) input) =>
            Task.FromResult($"embedding={input.Item1.Score}, cocitation={input.Item2.Score}");
    }

    private sealed class SignalsTrain : Train<string, string>
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            Chain<Echo>()
                .Parallel(p =>
                    p.Branch("embedding", b => b.Chain<ScoreEmbedding>())
                        .Branch("cocitation", b => b.Chain<ScoreCoCitation>())
                )
                .Chain<Combine>()
                .Resolve();
    }

    private sealed class Echo : Junction<string, string>
    {
        public override Task<string> Run(string input) => Task.FromResult(input);
    }

    private sealed class Meeting
    {
        public TaskCompletionSource A { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource B { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class MeetA(Meeting meeting) : Junction<string, Embedding>
    {
        public override async Task<Embedding> Run(string input)
        {
            meeting.A.SetResult();
            await meeting.B.Task;
            return new Embedding(1);
        }
    }

    private sealed class MeetB(Meeting meeting) : Junction<string, CoCitation>
    {
        public override async Task<CoCitation> Run(string input)
        {
            meeting.B.SetResult();
            await meeting.A.Task;
            return new CoCitation(1);
        }
    }

    private sealed class MeetingTrain(Meeting meeting) : Train<string, string>
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            Chain<Echo>()
                .Parallel(p =>
                    p.Branch("a", b => b.Chain(new MeetA(meeting)))
                        .Branch("b", b => b.Chain(new MeetB(meeting)))
                )
                .Chain<Combine>()
                .Resolve();
    }

    private sealed class Seen
    {
        public bool Joined { get; set; }
    }

    private sealed class ProducesThenSignals(Meeting meeting) : Junction<string, Embedding>
    {
        public override Task<Embedding> Run(string input)
        {
            meeting.A.SetResult();
            return Task.FromResult(new Embedding(1));
        }
    }

    private sealed class WaitsForSibling(Meeting meeting) : Junction<string, Functional.Unit>
    {
        public override async Task<Functional.Unit> Run(string input)
        {
            await meeting.A.Task;
            return Functional.Unit.Default;
        }
    }

    private sealed class IsolationTrain : Train<string, string>
    {
        private readonly Meeting _meeting = new();

        protected override Task<Either<Exception, string>> Junctions() =>
            Chain<Echo>()
                .Parallel(p =>
                    p.Branch("produces", b => b.Chain(new ProducesThenSignals(_meeting)))
                        .Branch(
                            "reads",
                            b => b.Chain(new WaitsForSibling(_meeting)).Chain<NeedsEmbedding>()
                        )
                        .OnFailure(BranchFailurePolicy.WaitForAll)
                )
                .Resolve();
    }

    private sealed class Length : Junction<string, int>
    {
        public override Task<int> Run(string input) => Task.FromResult(input.Length);
    }

    private sealed class Double : Junction<int, long>
    {
        public override Task<long> Run(int input) => Task.FromResult(input * 2L);
    }

    private sealed class PlainTrain : Train<string, long>
    {
        protected override Task<Either<Exception, long>> Junctions() =>
            Chain<Length>().Chain<Double>().Resolve();
    }

    private sealed class OneBranchTrain : Train<string, long>
    {
        protected override Task<Either<Exception, long>> Junctions() =>
            Chain<Echo>()
                .Parallel(p => p.Branch("only", b => b.Chain<Length>().Chain<Double>()))
                .Resolve();
    }

    private sealed class NestedTrain(Capture capture) : Train<string, string>
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            AddServices<IDecider>(new ScriptedDecider().Choose(Lane.Left))
                .Chain<Echo>()
                .Decide<string>(q => q.Choice<Lane>())
                .Switch<Lane>(s =>
                    s.When(
                            Lane.Left,
                            l =>
                                l.Parallel(p =>
                                    p.Branch(
                                            "outer",
                                            b =>
                                                b.Parallel(inner =>
                                                    inner.Branch(
                                                        "inner",
                                                        i => i.Chain(new Note(capture))
                                                    )
                                                )
                                        )
                                        .Branch("other", b => b.Chain(new Note(capture)))
                                )
                        )
                        .When(Lane.Right, r => r)
                )
                .Resolve();
    }

    private sealed class DecidingBranchTrain(Capture capture) : Train<string, string>
    {
        private sealed class Observer(Capture capture) : IDecisionObserver
        {
            public Task Decided(DecisionMade decision, CancellationToken cancellationToken)
            {
                capture.Note();
                return Task.CompletedTask;
            }

            public Task Routed(TrackRouted routing, CancellationToken cancellationToken)
            {
                capture.Note();
                return Task.CompletedTask;
            }
        }

        protected override Task<Either<Exception, string>> Junctions() =>
            AddServices<IDecider, IDecisionObserver>(
                    new ScriptedDecider().Choose(Lane.Left),
                    new Observer(capture)
                )
                .Chain<Echo>()
                .Parallel(p =>
                    p.Branch(
                            "decides",
                            b =>
                                b.Decide<string>(q => q.Choice<Lane>())
                                    .Switch<Lane>(s =>
                                        s.When(Lane.Left, l => l.Chain(new Note(capture)))
                                            .When(Lane.Right, r => r)
                                    )
                        )
                        .Branch("plain", b => b.Chain(new Note(capture)))
                )
                .Resolve();
    }

    private sealed class MarksJoined(Seen seen) : Junction<string, string>
    {
        public override Task<string> Run(string input)
        {
            seen.Joined = true;
            return Task.FromResult(input);
        }
    }

    /// <summary>Carries a class on its failure as a classified junction would.</summary>
    private sealed class FailsClassified(FailureClass failureClass)
        : Junction<string, Functional.Unit>
    {
        public override Task<Functional.Unit> Run(string input)
        {
            var e = new InvalidOperationException($"failed as {failureClass}");
            throw new TrainException(
                System.Text.Json.JsonSerializer.Serialize(
                    new TrainExceptionData
                    {
                        TrainName = "t",
                        TrainExternalId = "x",
                        Type = e.GetType().Name,
                        Junction = nameof(FailsClassified),
                        Message = e.Message,
                        FailureClass = failureClass,
                    }
                )
            );
        }
    }

    private sealed class FailingTrain(BranchFailurePolicy policy, Seen joined)
        : Train<string, string>
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            Chain<Echo>()
                .Parallel(p =>
                    p.Branch("permanent", b => b.Chain(new FailsClassified(FailureClass.Permanent)))
                        .Branch(
                            "transient",
                            b => b.Chain(new FailsClassified(FailureClass.Transient))
                        )
                        .OnFailure(policy)
                )
                .Chain(new MarksJoined(joined))
                .Resolve();
    }

    /// <summary>Completes only when <paramref name="token"/> is cancelled, by throwing.</summary>
    private static async Task UntilCancelled(CancellationToken token)
    {
        var cancelled = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        await using (token.Register(() => cancelled.TrySetCanceled(token)))
            await cancelled.Task;
    }

    private sealed class Waiter
    {
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Cancelled { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class WaitsForCancellation(Waiter waiter) : Junction<string, Embedding>
    {
        public override async Task<Embedding> Run(string input)
        {
            waiter.Entered.TrySetResult();

            try
            {
                await UntilCancelled(CancellationToken);
            }
            catch (OperationCanceledException)
            {
                waiter.Cancelled.TrySetResult();
                throw;
            }

            return new Embedding(0);
        }
    }

    private sealed class FailsOnceEntered(Waiter waiter) : Junction<string, Functional.Unit>
    {
        public override async Task<Functional.Unit> Run(string input)
        {
            await waiter.Entered.Task;
            throw new InvalidOperationException("the sibling fails");
        }
    }

    private sealed class CancelSiblingsTrain(Waiter waiter) : Train<string, string>
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            Chain<Echo>()
                .Parallel(p =>
                    p.Branch("waits", b => b.Chain(new WaitsForCancellation(waiter)))
                        .Branch("fails", b => b.Chain(new FailsOnceEntered(waiter)))
                )
                .Resolve();
    }

    private sealed class WaitsToo : Junction<string, CoCitation>
    {
        public override async Task<CoCitation> Run(string input)
        {
            await UntilCancelled(CancellationToken);
            return new CoCitation(0);
        }
    }

    private sealed class BothWaitTrain(Waiter waiter) : Train<string, string>
    {
        protected override Task<Either<Exception, string>> Junctions()
        {
            return Chain<Echo>()
                .Parallel(p =>
                    p.Branch("a", b => b.Chain(new WaitsForCancellation(waiter)))
                        .Branch("b", b => b.Chain(new WaitsToo()))
                )
                .Chain<Combine>()
                .Resolve();
        }
    }

    public sealed class Tenant
    {
        public string? Name { get; set; }
    }

    public sealed class Scoped(Tenant tenant) : IDisposable
    {
        public string? Tenant => tenant.Name;
        public bool Disposed { get; private set; }

        public void Dispose() => Disposed = true;
    }

    private sealed class TenantCopier : IBranchScopeInitializer
    {
        public Task Initialize(
            IServiceProvider run,
            IServiceProvider branch,
            CancellationToken cancellationToken
        )
        {
            branch.GetRequiredService<Tenant>().Name = run.GetRequiredService<Tenant>().Name;
            return Task.CompletedTask;
        }
    }

    private sealed class ThrowingInitializer : IBranchScopeInitializer
    {
        public Task Initialize(
            IServiceProvider run,
            IServiceProvider branch,
            CancellationToken cancellationToken
        ) => throw new InvalidOperationException("no tenant");
    }

    public sealed record LeftScope(Scoped Scoped);

    public sealed record RightScope(Scoped Scoped);

    public sealed record ScopeReport(Scoped Left, Scoped Right, bool LeftUsableAtTheJoin);

    private sealed class TakesScoped(Scoped scoped) : Junction<string, LeftScope>
    {
        public override Task<LeftScope> Run(string input) => Task.FromResult(new LeftScope(scoped));
    }

    private sealed class TakesScopedToo(Scoped scoped) : Junction<string, RightScope>
    {
        public override Task<RightScope> Run(string input) =>
            Task.FromResult(new RightScope(scoped));
    }

    private sealed class Report : Junction<(LeftScope, RightScope), ScopeReport>
    {
        public override Task<ScopeReport> Run((LeftScope, RightScope) input) =>
            Task.FromResult(
                new ScopeReport(
                    input.Item1.Scoped,
                    input.Item2.Scoped,
                    !input.Item1.Scoped.Disposed
                )
            );
    }

    private sealed class ScopedTrain(IServiceProvider services) : Train<string, ScopeReport>
    {
        protected override Task<Either<Exception, ScopeReport>> Junctions() =>
            AddServices(services)
                .Chain<Echo>()
                .Parallel(p =>
                    p.Branch("left", b => b.Chain<TakesScoped>())
                        .Branch("right", b => b.Chain<TakesScopedToo>())
                )
                .Chain<Report>()
                .Resolve();
    }

    private sealed class CollidingTrain : Train<string, int>
    {
        protected override Task<Either<Exception, int>> Junctions() =>
            Chain<Echo>()
                .Parallel(p =>
                    p.Branch("a", b => b.Chain<Length>()).Branch("b", b => b.Chain<Length>())
                )
                .Resolve();
    }

    private sealed class Shorten : Junction<string, string>
    {
        public override Task<string> Run(string input) => Task.FromResult(input[..1]);
    }

    private sealed class OverwritingTrain : Train<string, int>
    {
        protected override Task<Either<Exception, int>> Junctions() =>
            Chain<Echo>()
                .Parallel(p => p.Branch("a", b => b.Chain<Shorten>()))
                .Chain<Length>()
                .Resolve();
    }

    private sealed class NeedsEmbedding : Junction<Embedding, CoCitation>
    {
        public override Task<CoCitation> Run(Embedding input) => Task.FromResult(new CoCitation(1));
    }

    private sealed class SiblingReadingTrain : Train<string, string>
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            Chain<Echo>()
                .Parallel(p =>
                    p.Branch("produces", b => b.Chain<ScoreEmbedding>())
                        .Branch("reads", b => b.Chain<NeedsEmbedding>())
                )
                .Chain<Combine>()
                .Resolve();
    }

    private sealed class ShortCircuitInBranchTrain : Train<string, string>
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            Chain<Echo>().Parallel(p => p.Branch("a", b => b.ShortCircuit<Echo>())).Resolve();
    }

    private sealed class RootCallInBranchTrain : Train<string, string>
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            Chain<Echo>().Parallel(p => p.Branch("a", b => Chain<ScoreEmbedding>())).Resolve();
    }

    private sealed class NamedTrain(string name) : Train<string, string>
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            Chain<Echo>()
                .Parallel(p =>
                    name == "twice"
                        ? p.Branch(name, b => b.Chain<ScoreEmbedding>())
                            .Branch(name, b => b.Chain<ScoreCoCitation>())
                        : p.Branch(name, b => b.Chain<ScoreEmbedding>())
                )
                .Resolve();
    }

    private sealed class SharedInstanceTrain : Train<string, string>
    {
        private readonly Echo _echo = new();

        protected override Task<Either<Exception, string>> Junctions() =>
            Chain<Echo>()
                .Parallel(p => p.Branch("a", b => b.Chain(_echo)).Branch("b", b => b.Chain(_echo)))
                .Resolve();
    }

    private sealed class TwoSwitchesTrain(Capture capture) : Train<string, string>
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            AddServices<IDecider>(new ScriptedDecider().Choose(Lane.Left))
                .Chain<Echo>()
                .Parallel(p =>
                    p.Branch("a", b => Lanes(b, capture)).Branch("b", b => Lanes(b, capture))
                )
                .Resolve();

        private static MonadTask<string, string> Lanes(
            MonadTask<string, string> branch,
            Capture capture
        ) =>
            branch
                .Decide<string>(q => q.Choice<Lane>())
                .Switch<Lane>(s =>
                    s.When(Lane.Left, l => l.Chain(new Note(capture))).When(Lane.Right, r => r)
                );
    }

    public interface IShared
    {
        string Value { get; }
    }

    public sealed record Shared(string Value) : IShared;

    public sealed record SeenA(string Value);

    public sealed record SeenB(string Value);

    private sealed class ReadsSharedA(IShared shared) : Junction<string, SeenA>
    {
        public override Task<SeenA> Run(string input) => Task.FromResult(new SeenA(shared.Value));
    }

    private sealed class ReadsSharedB(IShared shared) : Junction<string, SeenB>
    {
        public override Task<SeenB> Run(string input) => Task.FromResult(new SeenB(shared.Value));
    }

    private sealed class JoinSeen : Junction<(SeenA, SeenB), string>
    {
        public override Task<string> Run((SeenA, SeenB) input) =>
            Task.FromResult($"{input.Item1.Value}|{input.Item2.Value}");
    }

    private sealed class SharedServiceTrain(Shared shared) : Train<string, string>
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            AddServices<IShared>(shared)
                .Chain<Echo>()
                .Parallel(p =>
                    p.Branch("a", b => b.Chain<ReadsSharedA>())
                        .Branch("b", b => b.Chain<ReadsSharedB>())
                )
                .Chain<JoinSeen>()
                .Resolve();
    }

    #endregion
}
