using FluentAssertions;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Junction;
using Trax.Core.Monad;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

/// <summary>
/// Decisions are declared with every track they can take, so the startup check sees them all, and
/// an answer the run cannot trust is refused rather than acted on. These pin both halves.
/// </summary>
public class DecisionTests : TestSetup
{
    private static IReadOnlyList<ChainFault> Verify(Train<string, bool> train) =>
        ChainVerification.Verify(train.DeclaredChain(), typeof(string), typeof(bool));

    private static Exception Failure(Either<Exception, bool> result) => result.Swap().ValueUnsafe();

    #region Verification

    [Test]
    public void Verify_WhatEveryTrackProduces_IsAvailableAfterTheRouting() =>
        Verify(
                Train(t =>
                    t.Switch<string, Lane>(s =>
                            s.When(Lane.Left, l => l.Chain<StringToInt>())
                                .When(Lane.Right, r => r.Chain<StringToInt>())
                        )
                        .Chain<IntToBool>()
                )
            )
            .Should()
            .BeEmpty();

    [Test]
    public void Verify_WhatOnlyOneTrackProduces_IsNotAvailableAfterTheRouting() =>
        Verify(
                Train(t =>
                    t.Switch<string, Lane>(s =>
                            s.When(Lane.Left, l => l.Chain<StringToInt>()).When(Lane.Right, r => r)
                        )
                        .Chain<IntToBool>()
                )
            )
            .Should()
            .ContainSingle()
            .Which.Should()
            .Match<ChainFault>(f => f.Junction == typeof(IntToBool) && f.Reason.Contains("Int32"));

    [Test]
    public void Verify_AStepInsideATrack_IsReportedAtTheRoutingStepNamingTheTrack()
    {
        var fault = Verify(
                Train(t =>
                    t.Gate<string, Flag>(g =>
                        g.Yes(y => y.Chain<StringToBool>()).No(n => n.Chain<IntToBool>())
                    )
                )
            )
            .Should()
            .ContainSingle()
            .Subject;

        fault.Kind.Should().Be(ChainStepKind.Gate);
        fault.Junction.Should().Be(typeof(IntToBool));
        fault.Reason.Should().StartWith("track 'No', step 0: needs 'System.Int32'");
    }

    [Test]
    public void Verify_RoutingOnADecisionNothingMade_IsReported() =>
        Verify(Train(t => t.Chain<StringToBool>().Switch<Lane>(s => s.When(Lane.Left, l => l))))
            .Should()
            .ContainSingle()
            .Which.Reason.Should()
            .Contain("routes on")
            .And.Contain("Ask it with Decide first");

    [Test]
    public void Verify_ADecideWithNoDecider_IsReported() =>
        ChainVerification
            .Verify(new NoDeciderTrain().DeclaredChain(), typeof(string), typeof(bool))
            .Should()
            .ContainSingle()
            .Which.Reason.Should()
            .Contain("needs a decider 'Trax.Core.Decisions.IDecider'");

    [Test]
    public void Verify_AShadowNothingSupplies_IsReported() =>
        Verify(
                Train(t =>
                    t.Decide<string>(q => q.Choice<Lane>().Shadow<IShadow>())
                        .Switch<Lane>(s =>
                            s.When(Lane.Left, l => l.Chain<StringToBool>())
                                .When(Lane.Right, r => r.Chain<StringToBool>())
                        )
                )
            )
            .Should()
            .ContainSingle()
            .Which.Reason.Should()
            .Contain("IShadow");

    [Test]
    public void Verify_EachQuestionInADecide_IsItsOwnStep()
    {
        var chain = Train(t =>
                t.Decide<string>(q => q.Choice<Lane>().YesNo<Flag>().Score<Level>())
                    .Chain<StringToBool>()
            )
            .DeclaredChain();

        chain
            .Steps.Where(s => s.Kind == ChainStepKind.Decide)
            .Select(s => s.Out)
            .Should()
            .Equal(
                typeof(ChoiceDecision<Lane>),
                typeof(YesNoDecision<Flag>),
                typeof(ScoreDecision<Level>)
            );
    }

    #endregion

    #region Declaration refusals

    [TestCaseSource(nameof(RefusedDeclarations))]
    public void DeclaredChain_ADeclarationThatCannotWork_IsRefused(
        Func<MonadTask<string, bool>, MonadTask<string, bool>> chain,
        string reason
    ) => Verify(Train(chain)).Should().Contain(f => f.IsRefusal && f.Reason.Contains(reason));

    private static IEnumerable<TestCaseData> RefusedDeclarations()
    {
        TestCaseData Case(
            string name,
            Func<MonadTask<string, bool>, MonadTask<string, bool>> chain,
            string reason
        ) => new TestCaseData(chain, reason).SetName($"DeclaredChain_Refuses_{name}");

        yield return Case(
            "ATrackDeclaredTwice",
            t => t.Switch<string, Lane>(s => s.When(Lane.Left, l => l).When(Lane.Left, l => l)),
            "declares the track 'Left' twice"
        );
        yield return Case(
            "ASwitchWithNoTracks",
            t => t.Switch<string, Lane>(s => s),
            "declares no tracks"
        );
        yield return Case(
            "AConfidenceOutsideZeroToOne",
            t => t.Switch<string, Lane>(s => s.When(Lane.Left, l => l, requireConfidence: 1.5)),
            "not between 0 and 1"
        );
        yield return Case(
            "AQuestionWithNoWords",
            t => t.Switch<string, Unasked>(s => s.When(Unasked.A, a => a)),
            "asks nothing about"
        );
        yield return Case(
            "AQuestionAskedTwice",
            t => t.Decide<string>(q => q.Choice<Lane>().Choice<Lane>()),
            "asks about 'Lane' twice"
        );
        yield return Case(
            "ADecideAskingNothing",
            t => t.Decide<string>(q => q),
            "asks no questions"
        );
        yield return Case(
            "AGateWithNoYesTrack",
            t => t.Gate<string, Flag>(g => g.No(n => n)),
            "declares no Yes track"
        );
        yield return Case(
            "AGateWhoseBarsCross",
            t => t.Gate<string, Flag>(g => g.Yes(y => y, atLeast: 0.4).No(n => n, below: 0.6)),
            "would take both"
        );
        yield return Case(
            "AScaleWithNothingForItsLowestLevel",
            t => t.Scale<string, Level>(s => s.AtLeast(Level.High, h => h)),
            "has no track from its lowest level, 'Low'"
        );
        yield return Case(
            "ATrackOnAValueTheEnumDoesNotDefine",
            t => t.Switch<string, Lane>(s => s.When(Lane.Left, l => l).When((Lane)7, l => l)),
            "declares a track for '7', which is not a member of 'Lane'"
        );
        yield return Case(
            "ATrackOnAValueTheEnumDoesNotDefine_InASwitchThatAsksNothing",
            t =>
                t.Decide<string>(q => q.Choice<Lane>())
                    .Switch<Lane>(s => s.When(Lane.Left, l => l).When((Lane)7, l => l)),
            "declares a track for '7', which is not a member of 'Lane'"
        );
        yield return Case(
            "AScaleTrackOnAValueTheEnumDoesNotDefine",
            t =>
                t.Scale<string, Level>(s => s.AtLeast(Level.Low, l => l).AtLeast((Level)9, l => l)),
            "declares a track for '9', which is not a member of 'Level'"
        );
        yield return Case(
            "AScaleOnAnEnumWithNoMembers",
            t => t.Scale<string, NoLevels>(s => s),
            "rates on 'NoLevels', which has fewer than two levels"
        );
        yield return Case(
            "AScaleThatAsksNothingOnAnEnumWithNoMembers",
            t => t.Scale<NoLevels>(s => s),
            "routes on 'NoLevels', which has fewer than two levels"
        );
        yield return Case(
            "AScaleThatAsksNothingOnAnEnumWithOneMember",
            t => t.Scale<OneLevel>(s => s.AtLeast(OneLevel.Only, o => o)),
            "routes on 'OneLevel', which has fewer than two levels"
        );
        yield return Case(
            "AShadowedStateJsonCannotReadBack",
            t =>
                t.Chain<StringToHolder>()
                    .Decide<Holder>(q => q.Choice<Lane>().Shadow<IShadow>())
                    .Chain<StringToBool>(),
            "its member 'Inner' is declared as 'IInner', an interface, which JSON cannot read back"
        );
        yield return Case(
            "AShadowedStateJsonCannotWrite",
            t =>
                t.Chain<StringToCallback>()
                    .Decide<Callback>(q => q.Choice<Lane>().Shadow<IShadow>())
                    .Chain<StringToBool>(),
            "its member 'Notify' is a 'Action', which JSON cannot write"
        );
        yield return Case(
            "ATrackNamingSomethingNotAJunction",
            t => t.Switch<string, Lane>(s => s.When(Lane.Left, l => l.Chain<NotAJunction>())),
            "track 'Left': Chain names NotAJunction"
        );
    }

    [Test]
    public void DeclaredChain_AStateJsonCannotReadBack_IsNoProblemWithoutShadows() =>
        Verify(
                Train(t =>
                    t.Chain<StringToHolder>()
                        .Decide<Holder>(q => q.Choice<Lane>())
                        .Chain<StringToBool>()
                )
            )
            .Should()
            .NotContain(f => f.Reason.Contains("JSON"));

    [Test]
    public void DeclaredChain_AShadowedStateJsonCanCopy_IsNoProblem() =>
        Verify(
                Train(t =>
                    t.Chain<StringToParcel>()
                        .Decide<Parcel>(q => q.Choice<Lane>().Shadow<IShadow>())
                        .Chain<StringToBool>()
                )
            )
            .Should()
            .NotContain(f => f.Reason.Contains("JSON"));

    [Test]
    public async Task Run_AShadowedStateJsonCannotReadBack_IsRefusedBeforeTheDeciderIsAsked()
    {
        var decider = new ScriptedDecider().Choose(Lane.Left);

        var failure = Failure(
            await Train(
                    t =>
                        t.Chain<StringToHolder>()
                            .Decide<Holder>(q => q.Choice<Lane>().Shadow<IShadow>())
                            .Chain<StringToBool>(),
                    decider
                )
                .RunEither("x")
        );

        failure.Message.Should().Contain("cannot read back");
        decider.Requests.Should().BeEmpty();
    }

    [Test]
    public async Task Run_ATrackOnAValueTheEnumDoesNotDefine_IsRefusedBeforeTheDeciderIsAsked()
    {
        var decider = new ScriptedDecider().Choose(Lane.Left);

        var failure = Failure(
            await Train(
                    t =>
                        t.Switch<string, Lane>(s =>
                            s.When(Lane.Left, l => l.Chain<StringToBool>())
                                .When((Lane)7, l => l.Chain<StringToBool>())
                        ),
                    decider
                )
                .RunEither("x")
        );

        failure.Message.Should().Contain("'7', which is not a member of 'Lane'");
        decider.Requests.Should().BeEmpty("a question offering the wrong option is never asked");
    }

    [Test]
    public async Task Run_AScaleThatAsksNothingOnAnEnumWithNoMembers_IsRefusedNotThrown() =>
        Failure(
            await Train(t => t.Scale<NoLevels>(s => s.Otherwise(o => o.Chain<StringToBool>())))
                .RunEither("x")
        )
            .Message.Should()
            .Contain("fewer than two levels");

    [Test]
    public void DeclaredChain_AsksNoDeciderAndRunsNoTrack()
    {
        var decider = new ScriptedDecider().Throws(new InvalidOperationException("asked"));

        var chain = Train(
                t =>
                    t.Switch<string, Lane>(s =>
                        s.When(Lane.Left, l => l.Chain<Explodes>())
                            .When(Lane.Right, r => r.Chain<Explodes>())
                    ),
                decider
            )
            .DeclaredChain();

        chain.Refusals.Should().BeEmpty();
        decider.Requests.Should().BeEmpty();
    }

    #endregion

    #region Answers the run will not act on

    [TestCaseSource(nameof(UnfitAnswers))]
    public async Task Run_AnAnswerThatDoesNotFit_FailsTheRunTransiently(
        Answer answer,
        string reason
    )
    {
        var decider = new ScriptedDecider().Answer(QuestionKey.For<Lane>(), _ => answer);

        var failure = Failure(
            await Train(
                    t =>
                        t.Switch<string, Lane>(s =>
                            s.When(Lane.Left, l => l.Chain<StringToBool>())
                                .Otherwise(o => o.Chain<StringToBool>())
                        ),
                    decider
                )
                .RunEither("x")
        );

        failure.Message.Should().Contain(reason);
        ((TrainExceptionData)failure.Data["TrainExceptionData"]!)
            .FailureClass.Should()
            .Be(FailureClass.Transient, "a model asked again may well answer properly");
    }

    private static IEnumerable<TestCaseData> UnfitAnswers()
    {
        yield return new TestCaseData(
            new ChoiceAnswer("Banana"),
            "'Banana', which is not one of its options"
        ).SetName("Run_Refuses_AnOptionThatDoesNotExist");
        yield return new TestCaseData(
            new ChoiceAnswer("1"),
            "'1', which is not one of its options"
        ).SetName("Run_Refuses_AnOptionGivenAsANumber");
        yield return new TestCaseData(
            new ChoiceAnswer("Left", double.NaN),
            "not between 0 and 1"
        ).SetName("Run_Refuses_ANaNConfidence");
        yield return new TestCaseData(new ChoiceAnswer("Left", 1.2), "not between 0 and 1").SetName(
            "Run_Refuses_AConfidenceAboveOne"
        );
        yield return new TestCaseData(
            new ChoiceAnswer("Left", 1, new Dictionary<string, double> { ["Left"] = -0.1 }),
            "probability for 'Left'"
        ).SetName("Run_Refuses_ANegativeProbability");
        yield return new TestCaseData(new YesNoAnswer(0.9), "not a choice").SetName(
            "Run_Refuses_TheWrongKindOfAnswer"
        );
    }

    [Test]
    public async Task Run_AQuestionLeftUnanswered_FailsTheRunTransiently()
    {
        var failure = Failure(
            await Train(
                    t =>
                        t.Switch<string, Lane>(s =>
                            s.When(Lane.Left, l => l.Chain<StringToBool>())
                        ),
                    new ScriptedDecider()
                )
                .RunEither("x")
        );

        failure.Message.Should().Contain($"gave no answer to '{QuestionKey.For<Lane>()}'");
        ((TrainExceptionData)failure.Data["TrainExceptionData"]!)
            .FailureClass.Should()
            .Be(FailureClass.Transient, "a model that dropped a question may answer it next time");
    }

    [Test]
    public async Task Run_ADeclarationThatCannotWork_StaysPermanent() =>
        (
            (TrainExceptionData)
                Failure(
                    await Train(t =>
                            t.Switch<string, Lane>(s =>
                                s.When(Lane.Left, l => l).When(Lane.Left, l => l)
                            )
                        )
                        .RunEither("x")
                ).Data["TrainExceptionData"]!
        )
            .FailureClass.Should()
            .Be(FailureClass.Permanent);

    [TestCase(-0.5)]
    [TestCase(2.01)]
    public async Task Run_AScoreOffTheScale_FailsTheRun(double score) =>
        Failure(
            await Train(
                    t =>
                        t.Scale<string, Level>(s =>
                            s.AtLeast(Level.Low, l => l.Chain<StringToBool>())
                        ),
                    new ScriptedDecider().Score<Level>(score)
                )
                .RunEither("x")
        )
            .Message.Should()
            .Contain("outside its levels 0 to 2");

    [Test]
    public async Task Run_AfterAnEarlierFailure_DoesNotAskTheDecider()
    {
        var decider = new ScriptedDecider().Choose(Lane.Left);

        var failure = Failure(
            await Train(
                    t =>
                        t.Chain<Fails>()
                            .Switch<string, Lane>(s =>
                                s.When(Lane.Left, l => l.Chain<StringToBool>())
                            ),
                    decider
                )
                .RunEither("x")
        );

        failure.Message.Should().Be("upstream failed");
        decider.Requests.Should().BeEmpty();
    }

    [Test]
    public async Task Run_ADeclarationTheStartupCheckRefuses_IsRefusedByTheRunToo() =>
        Failure(
            await Train(
                    t =>
                        t.Switch<string, Lane>(s =>
                            s.When(Lane.Left, l => l).When(Lane.Left, l => l)
                        ),
                    new ScriptedDecider().Choose(Lane.Left)
                )
                .RunEither("x")
        )
            .Message.Should()
            .Contain("twice");

    [Test]
    public async Task Run_TheCancellationTokenReachesTheDecider()
    {
        using var cts = new CancellationTokenSource();
        var decider = new ScriptedDecider().Choose(Lane.Left);

        await Train(
                t => t.Switch<string, Lane>(s => s.When(Lane.Left, l => l.Chain<StringToBool>())),
                decider
            )
            .Run("x", cts.Token);

        decider.LastToken.Should().Be(cts.Token);
    }

    #endregion

    #region Bands

    [TestCase(0.69, "Unsure")]
    [TestCase(0.7, "Yes")]
    [TestCase(0.3, "Unsure")]
    [TestCase(0.29, "No")]
    public async Task Gate_TakesTheTrackItsBarsSay(double probability, string track)
    {
        var result = await Train(
                t =>
                    t.Gate<string, Flag>(g =>
                            g.Yes(y => y.Chain<Says>(new Says("Yes")), atLeast: 0.7)
                                .No(n => n.Chain<Says>(new Says("No")), below: 0.3)
                                .Unsure(u => u.Chain<Says>(new Says("Unsure")))
                        )
                        .Chain<Heard>(),
                new ScriptedDecider().YesNo<Flag>(probability)
            )
            .RunEither(Key);

        result.IsRight.Should().BeTrue();
        Says.Ran[Key].Should().Be(track);
    }

    [Test]
    public async Task Gate_WithNoUnsureTrack_FailsInTheBandBetweenItsBars() =>
        Failure(
            await Train(
                    t =>
                        t.Gate<string, Flag>(g =>
                            g.Yes(y => y.Chain<StringToBool>(), atLeast: 0.7)
                                .No(n => n.Chain<StringToBool>(), below: 0.3)
                        ),
                    new ScriptedDecider().YesNo<Flag>(0.5)
                )
                .RunEither("x")
        )
            .Message.Should()
            .Contain("declares no Unsure track");

    [TestCase(0.0, "Low")]
    [TestCase(0.49, "Low")]
    [TestCase(0.5, "Low", Description = "rounds to Medium, which has no track of its own")]
    [TestCase(1.49, "Low")]
    [TestCase(1.5, "High")]
    [TestCase(2.0, "High")]
    public async Task Scale_TakesTheHighestTrackAtOrBelowTheNearestLevel(double score, string track)
    {
        var result = await Train(
                t =>
                    t.Scale<string, Level>(s =>
                            s.AtLeast(Level.Low, l => l.Chain<Says>(new Says("Low")))
                                .AtLeast(Level.High, h => h.Chain<Says>(new Says("High")))
                        )
                        .Chain<Heard>(),
                new ScriptedDecider().Score<Level>(score)
            )
            .RunEither(Key);

        result.IsRight.Should().BeTrue();
        Says.Ran[Key].Should().Be(track);
    }

    [Test]
    public async Task Scale_BelowItsConfidenceBar_TakesOtherwise()
    {
        var result = await Train(
                t =>
                    t.Scale<string, Level>(s =>
                            s.AtLeast(Level.Low, l => l.Chain<Says>(new Says("Low")))
                                .RequireConfidence(0.8)
                                .Otherwise(o => o.Chain<Says>(new Says("Otherwise")))
                        )
                        .Chain<Heard>(),
                new ScriptedDecider().Score<Level>(1, confidence: 0.5)
            )
            .RunEither(Key);

        result.IsRight.Should().BeTrue();
        Says.Ran[Key].Should().Be("Otherwise");
    }

    #endregion

    #region Fixtures

    private static string Key => TestContext.CurrentContext.Test.ID;

    [Asks("Which lane?")]
    public enum Lane
    {
        Left,
        Right,
    }

    [Asks("How high?")]
    public enum Level
    {
        Low,
        Medium,
        High,
    }

    public enum Unasked
    {
        A,
    }

    [Asks("Where on nothing?")]
    public enum NoLevels { }

    [Asks("Where on one level?")]
    public enum OneLevel
    {
        Only,
    }

    [Asks("Is the flag up?")]
    public sealed class Flag;

    public interface IShadow : IDecider;

    private static Train<string, bool> Train(
        Func<MonadTask<string, bool>, MonadTask<string, bool>> chain,
        IDecider? decider = null
    ) => new DeclaredTrain(chain, decider ?? new ScriptedDecider());

    private sealed class DeclaredTrain(
        Func<MonadTask<string, bool>, MonadTask<string, bool>> chain,
        IDecider decider
    ) : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            chain(AddServices(decider).Chain<Start>()).Resolve();
    }

    private sealed class NoDeciderTrain : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            Switch<string, Lane>(s => s.When(Lane.Left, l => l.Chain<StringToBool>())).Resolve();
    }

    /// <summary>Passes the input through, so a chain can start with any step after it.</summary>
    private class Start : Junction<string, string>
    {
        public override Task<string> Run(string input) => Task.FromResult(input);
    }

    private class StringToInt : Junction<string, int>
    {
        public override Task<int> Run(string input) => Task.FromResult(input.Length);
    }

    private class StringToBool : Junction<string, bool>
    {
        public override Task<bool> Run(string input) => Task.FromResult(true);
    }

    private class IntToBool : Junction<int, bool>
    {
        public override Task<bool> Run(int input) => Task.FromResult(input > 0);
    }

    private class Explodes : Junction<string, bool>
    {
        public override Task<bool> Run(string input) =>
            throw new InvalidOperationException("this track was not chosen");
    }

    private class Fails : Junction<string, string>
    {
        public override Task<string> Run(string input) =>
            throw new InvalidOperationException("upstream failed");
    }

    /// <summary>Records which track ran, and produces the value the next step reads.</summary>
    private class Says(string track) : Junction<string, Heard.Marker>
    {
        /// <summary>The track that ran, by the run's input, so parallel tests do not collide.</summary>
        public static System.Collections.Concurrent.ConcurrentDictionary<
            string,
            string
        > Ran { get; } = new();

        public override Task<Heard.Marker> Run(string input)
        {
            Ran[input] = track;
            return Task.FromResult(new Heard.Marker());
        }
    }

    private class Heard : Junction<Heard.Marker, bool>
    {
        public sealed class Marker;

        public override Task<bool> Run(Marker input) => Task.FromResult(true);
    }

    private class NotAJunction;

    public interface IInner;

    /// <summary>A state JSON writes but cannot read back: a member is declared as an interface.</summary>
    public sealed class Holder
    {
        public string Name { get; set; } = "";

        public IInner? Inner { get; set; }
    }

    /// <summary>A state JSON cannot write: a member is a delegate.</summary>
    public sealed class Callback
    {
        public Action? Notify { get; set; }
    }

    /// <summary>A state JSON copies: a record read back through its constructor, with a list.</summary>
    public sealed record Parcel(string Id, decimal Weight, IReadOnlyList<string> Tags);

    private class StringToHolder : Junction<string, Holder>
    {
        public override Task<Holder> Run(string input) => Task.FromResult(new Holder());
    }

    private class StringToCallback : Junction<string, Callback>
    {
        public override Task<Callback> Run(string input) => Task.FromResult(new Callback());
    }

    private class StringToParcel : Junction<string, Parcel>
    {
        public override Task<Parcel> Run(string input) =>
            Task.FromResult(new Parcel(input, 1m, ["fragile"]));
    }

    #endregion
}
