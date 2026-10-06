using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Decisions;
using Trax.Core.Exceptions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Monad;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Train;

/// <summary>
/// What a deciding run does around the decision itself: shadows that must never hold it up,
/// replays that no longer fit, observers that fail, a cancelled run, and how a shadow's agreement
/// is judged against the step's own routing.
///
/// <para>The replay tests pin core/0004 (docs/adr/0004-a-recorded-answer-replays-only-into-the-same-state.md):
/// a recorded answer is replayed only into an asking whose state hashes exactly as the state it
/// was given about did, and is asked afresh otherwise.</para>
/// </summary>
[Property("adr", "docs/adr/0004-a-recorded-answer-replays-only-into-the-same-state.md")]
public class DecisionRuntimeTests : TestSetup
{
    /// <summary>Long enough never to be reached by a run that is working, so it only names a hang.</summary>
    private static readonly TimeSpan Hang = TimeSpan.FromSeconds(30);

    #region Shadows

    [Test]
    public async Task Shadow_ThatNeverAnswersAndIgnoresCancellation_DoesNotHoldUpTheRun()
    {
        var shadow = new HangingShadow();
        var observer = new RecordingObserver();
        var log = new List<string>();

        var result = await Run(
                t =>
                    t.Decide<string>(q =>
                            q.Choice<Lane>().Shadow<IShadow>().WaitForShadows(TimeSpan.Zero)
                        )
                        .Switch<Lane>(s => Lanes(s, log)),
                new Services()
                    .With<IDecider>(new ScriptedDecider().Choose(Lane.Left))
                    .With<IShadow>(shadow)
                    .With<IDecisionObserver>(observer)
            )
            .WaitAsync(Hang);

        result.IsRight.Should().BeTrue();
        log.Should().Equal("Left");
        await shadow.Entered.Task.WaitAsync(Hang);
        shadow.Token.IsCancellationRequested.Should().BeTrue("a shadow not waited for is stopped");
        observer
            .Decisions.Should()
            .ContainSingle()
            .Which.Shadows.Should()
            .ContainSingle()
            .Which.Should()
            .Match<ShadowAnswer>(s =>
                s.Answer == null && !s.Agrees && s.Error!.Contains("did not answer within 0s")
            );
    }

    [Test]
    public async Task Shadow_ThatNeverAnswers_DoesNotKeepItsCancellationSourceOnceTheStepGivesUp()
    {
        var shadow = new HangingShadow();

        var result = await Run(
                t =>
                    t.Decide<string>(q =>
                            q.Choice<Lane>().Shadow<IShadow>().WaitForShadows(TimeSpan.Zero)
                        )
                        .Switch<Lane>(s => Lanes(s, [])),
                new Services()
                    .With<IDecider>(new ScriptedDecider().Choose(Lane.Left))
                    .With<IShadow>(shadow)
            )
            .WaitAsync(Hang);

        result.IsRight.Should().BeTrue();
        await shadow.Entered.Task.WaitAsync(Hang);
        var handle = () => shadow.Token.WaitHandle;
        handle
            .Should()
            .Throw<ObjectDisposedException>(
                "the source linked to the run's token is disposed when the step stops waiting, "
                    + "not when a shadow that may never return does"
            );
    }

    [Test]
    public async Task Shadow_FromTheContainer_SharesNoScopedServiceWithTheLiveDecider()
    {
        var made = new List<NotThreadSafe>();
        var liveEntered = new TaskCompletionSource();
        var shadowDone = new TaskCompletionSource();
        var observer = new RecordingObserver();

        var services = new ServiceCollection();
        services.AddScoped(_ =>
        {
            var resource = new NotThreadSafe();
            lock (made)
                made.Add(resource);
            return resource;
        });
        services.AddScoped<IDecider>(p => new HoldingDecider(
            p.GetRequiredService<NotThreadSafe>(),
            liveEntered,
            shadowDone
        ));
        services.AddScoped<IShadow>(p => new ConcurrentShadow(
            p.GetRequiredService<NotThreadSafe>(),
            liveEntered,
            shadowDone
        ));
        services.AddSingleton<IDecisionObserver>(observer);

        await using var root = services.BuildServiceProvider(validateScopes: true);
        await using var run = root.CreateAsyncScope();

        var result = await new DecidingTrain(
            t =>
                t.Decide<string>(q => q.Choice<Lane>().Shadow<IShadow>())
                    .Switch<Lane>(s => Lanes(s, [])),
            run.ServiceProvider
        )
            .RunEither("x")
            .WaitAsync(Hang);

        result.IsRight.Should().BeTrue();
        observer
            .Decisions.Single()
            .Shadows.Single()
            .Should()
            .Match<ShadowAnswer>(s => s.Error == null && s.Agrees);
        made.Should().HaveCount(2, "the shadow gets its own instance of a scoped service");
        made.Count(r => r.Disposed)
            .Should()
            .Be(1, "the shadow's scope is disposed when it ends, the run's when the run's is");
    }

    [Test]
    public async Task Shadow_IsHandedACopyOfTheStateOfItsOwn()
    {
        var basket = new Basket { Name = "original" };
        basket.Items.Add("apple");
        var live = new SeeingDecider();
        var first = new MutatingShadow("first");
        var second = new MutatingShadow("second");
        var observer = new RecordingObserver();

        var result = await Run(
                t =>
                    t.Chain(new MakeBasket(basket))
                        .Decide<Basket>(q =>
                            q.Choice<Lane>().Shadow<IShadow>().Shadow<IOtherShadow>()
                        )
                        .Switch<Lane>(s => Lanes(s, [])),
                new Services()
                    .With<IDecider>(live)
                    .With<IShadow>(first)
                    .With<IOtherShadow>(second)
                    .With<IDecisionObserver>(observer)
            )
            .WaitAsync(Hang);

        result.IsRight.Should().BeTrue();
        live.Seen.Should().BeSameAs(basket, "the live decider is handed the run's own state");
        first.Seen.Should().NotBeSameAs(basket).And.NotBeSameAs(second.Seen);
        second.Seen.Should().NotBeSameAs(basket);
        first.Saw.Should().Be("original: apple", "the copy carries the state, list included");
        second.Saw.Should().Be("original: apple", "the other shadow's changes are not in it");
        basket.Name.Should().Be("original", "a shadow's changes never reach the run");
        basket.Items.Should().Equal("apple");
        observer.Decisions.Single().Shadows.Should().OnlyContain(s => s.Error == null && s.Agrees);
    }

    [Test]
    public async Task Shadow_WhoseStateCannotBeWrittenAsJson_IsRecordedAsNotAnsweringAndTheRunGoesOn()
    {
        var shadow = new ScriptedShadow(new ScriptedDecider().Choose(Lane.Left));
        var observer = new RecordingObserver();

        var result = await Run(
                t =>
                    t.Chain(new MakeParcel(new CallbackParcel()))
                        .Decide<IParcel>(q => q.Choice<Lane>().Shadow<IShadow>())
                        .Switch<Lane>(s => Lanes(s, [])),
                new Services()
                    .With<IDecider>(new ScriptedDecider().Choose(Lane.Left))
                    .With<IShadow>(shadow)
                    .With<IDecisionObserver>(observer)
            )
            .WaitAsync(Hang);

        result.IsRight.Should().BeTrue();
        shadow.Inner.Requests.Should().BeEmpty("a shadow with no copy of its own is not asked");
        observer
            .Decisions.Single()
            .Shadows.Single()
            .Should()
            .Match<ShadowAnswer>(s =>
                s.Answer == null && !s.Agrees && s.Error!.Contains("could not be made through JSON")
            );
    }

    [Test]
    public async Task Shadow_WhoseStateCannotBeReadBack_IsRecordedAsNotAnsweringAndTheRunGoesOn()
    {
        var shadow = new ScriptedShadow(new ScriptedDecider().Choose(Lane.Left));
        var observer = new RecordingObserver();

        var result = await Run(
                t =>
                    t.Chain(
                            new MakeParcel(
                                new WrappingParcel { Inner = new WrappingParcel.Plain() }
                            )
                        )
                        .Decide<IParcel>(q => q.Choice<Lane>().Shadow<IShadow>())
                        .Switch<Lane>(s => Lanes(s, [])),
                new Services()
                    .With<IDecider>(new ScriptedDecider().Choose(Lane.Left))
                    .With<IShadow>(shadow)
                    .With<IDecisionObserver>(observer)
            )
            .WaitAsync(Hang);

        result.IsRight.Should().BeTrue();
        shadow.Inner.Requests.Should().BeEmpty();
        observer
            .Decisions.Single()
            .Shadows.Single()
            .Error.Should()
            .Contain("could not be made through JSON");
    }

    [Test]
    public async Task Shadow_ThatIgnoresCancellationAndNeverReturns_HasItsScopeDisposedSoonAfter()
    {
        var made = new TaskCompletionSource<Tracked>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var services = new ServiceCollection();
        services.AddScoped(_ =>
        {
            var tracked = new Tracked();
            made.TrySetResult(tracked);
            return tracked;
        });
        services.AddScoped<IShadow>(p => new HangingWith(p.GetRequiredService<Tracked>()));
        services.AddSingleton<IDecider>(new ScriptedDecider().Choose(Lane.Left));

        await using var root = services.BuildServiceProvider(validateScopes: true);
        await using var run = root.CreateAsyncScope();

        var result = await new DecidingTrain(
            t =>
                t.Decide<string>(q =>
                        q.Choice<Lane>().Shadow<IShadow>().WaitForShadows(TimeSpan.Zero)
                    )
                    .Switch<Lane>(s => Lanes(s, [])),
            run.ServiceProvider
        )
            .RunEither("x")
            .WaitAsync(Hang);

        result.IsRight.Should().BeTrue();
        var tracked = await made.Task.WaitAsync(Hang);
        var disposed = () => tracked.Disposed.Task.WaitAsync(Hang);
        await disposed
            .Should()
            .NotThrowAsync("a shadow that never returns does not keep its scope for ever");
    }

    [Test]
    public async Task Shadow_ThatNeverAnswers_DoesNotHoldUpALiveDeciderThatFails()
    {
        var result = await Run(
                t =>
                    t.Decide<string>(q => q.Choice<Lane>().Shadow<IShadow>())
                        .Switch<Lane>(s => Lanes(s, [])),
                new Services()
                    .With<IDecider>(new ScriptedDecider().Throws(new TimeoutException("down")))
                    .With<IShadow>(new HangingShadow())
            )
            .WaitAsync(Hang);

        Failure(result).Should().BeOfType<TimeoutException>();
    }

    [Test]
    public async Task Shadow_IsNotAskedAQuestionWhoseAnswerIsReplayed()
    {
        var shadow = new ScriptedShadow(new ScriptedDecider().Choose(Lane.Right).YesNo<Flag>(0.9));
        var observer = new RecordingObserver();
        Func<MonadTask<string, bool>, MonadTask<string, bool>> chain = t =>
            t.Decide<string>(q => q.Choice<Lane>().YesNo<Flag>().Shadow<IShadow>())
                .Switch<Lane>(s => Lanes(s, []));
        var journal = await Recorded(
            chain,
            new Services().With<IShadow>(new ScriptedShadow(new ScriptedDecider())),
            new ScriptedDecider().Choose(Lane.Right).YesNo<Flag>(0.5)
        );

        var result = await Run(
            chain,
            new Services()
                .With<IDecider>(new ScriptedDecider().YesNo<Flag>(0.1))
                .With<IShadow>(shadow)
                .With<IDecisionReplay>(
                    journal.Hold(QuestionKey.For<Lane>(), new ChoiceAnswer("Left"))
                )
                .With<IDecisionObserver>(observer)
        );

        result.IsRight.Should().BeTrue();
        shadow
            .Inner.Requests.Should()
            .ContainSingle()
            .Which.Questions.Select(q => q.Key)
            .Should()
            .Equal(QuestionKey.For<Flag>());
        observer
            .Decisions.Select(d => (d.Question.Key, d.Replayed, d.Shadows.Count))
            .Should()
            .Equal((QuestionKey.For<Lane>(), true, 0), (QuestionKey.For<Flag>(), false, 1));
    }

    [Test]
    public async Task Shadow_WhenEveryAnswerIsReplayed_IsNotAskedAtAll()
    {
        var shadow = new ScriptedShadow(new ScriptedDecider().Choose(Lane.Right));
        Func<MonadTask<string, bool>, MonadTask<string, bool>> chain = t =>
            t.Decide<string>(q => q.Choice<Lane>().Shadow<IShadow>())
                .Switch<Lane>(s => Lanes(s, []));
        var journal = await Recorded(
            chain,
            new Services().With<IShadow>(new ScriptedShadow(new ScriptedDecider())),
            new ScriptedDecider().Choose(Lane.Left)
        );

        var result = await Run(
            chain,
            new Services()
                .With<IDecider>(new ScriptedDecider())
                .With<IShadow>(shadow)
                .With<IDecisionReplay>(
                    journal.Hold(QuestionKey.For<Lane>(), new ChoiceAnswer("Left"))
                )
        );

        result.IsRight.Should().BeTrue();
        shadow.Inner.Requests.Should().BeEmpty();
    }

    [Test]
    public async Task Shadow_NobodyRegistered_IsRefusedByTheRunAsTheStartupCheckRefusesIt()
    {
        var failure = Failure(
            await Run(
                t =>
                    t.Decide<string>(q => q.Choice<Lane>().Shadow<IShadow>())
                        .Switch<Lane>(s => Lanes(s, [])),
                new Services().With<IDecider>(new ScriptedDecider().Choose(Lane.Left))
            )
        );

        failure.Message.Should().Contain("needs a shadow decider").And.Contain("IShadow");
        ClassOf(failure).Should().Be(FailureClass.Permanent);
    }

    [Test]
    public void WaitForShadows_ANegativeWait_IsRefused() =>
        ChainVerification
            .Verify(
                new DecidingTrain(
                    t =>
                        t.Decide<string>(q =>
                                q.Choice<Lane>()
                                    .Shadow<IShadow>()
                                    .WaitForShadows(TimeSpan.FromSeconds(-1))
                            )
                            .Switch<Lane>(s => Lanes(s, [])),
                    new Services()
                ).DeclaredChain(),
                typeof(string),
                typeof(bool)
            )
            .Should()
            .Contain(f => f.IsRefusal && f.Reason.Contains("Wait a bounded, non-negative time"));

    #endregion

    #region Shadow agreement follows the step's routing

    [TestCase(0.55, 0.45, true, Description = "both fall in the Unsure band")]
    [TestCase(0.75, 0.65, false, Description = "Yes against Unsure, both above one half")]
    [TestCase(0.2, 0.1, true, Description = "both below the No bar")]
    public async Task ShadowAgreement_OnAGate_IsTakingTheSameTrack(
        double live,
        double shadow,
        bool agrees
    )
    {
        var observer = new RecordingObserver();

        await Run(
            t =>
                t.Gate<string, Flag>(g =>
                        g.Yes(y => y, atLeast: 0.7)
                            .No(n => n, below: 0.3)
                            .Unsure(u => u)
                            .Shadow<IShadow>()
                    )
                    .Chain<StringToBool>(),
            Shadowed(
                new ScriptedDecider().YesNo<Flag>(live),
                new ScriptedDecider().YesNo<Flag>(shadow),
                observer
            )
        );

        observer.Decisions.Single().Shadows.Single().Agrees.Should().Be(agrees);
    }

    [TestCase(0.0, 1.0, true, Description = "Low and Medium share the Low track")]
    [TestCase(1.0, 2.0, false, Description = "Medium takes Low, High takes High")]
    public async Task ShadowAgreement_OnAScale_IsReachingTheSameBand(
        double live,
        double shadow,
        bool agrees
    )
    {
        var observer = new RecordingObserver();

        await Run(
            t =>
                t.Scale<string, Level>(s =>
                        s.AtLeast(Level.Low, l => l).AtLeast(Level.High, h => h).Shadow<IShadow>()
                    )
                    .Chain<StringToBool>(),
            Shadowed(
                new ScriptedDecider().Score<Level>(live),
                new ScriptedDecider().Score<Level>(shadow),
                observer
            )
        );

        observer.Decisions.Single().Shadows.Single().Agrees.Should().Be(agrees);
    }

    [TestCase(0.9, 0.5, false, Description = "the shadow's Left is below Left's own bar")]
    [TestCase(0.5, 0.4, true, Description = "both below the bar, so both go to Otherwise")]
    public async Task ShadowAgreement_OnASwitch_HonoursEachTracksBar(
        double live,
        double shadow,
        bool agrees
    )
    {
        var observer = new RecordingObserver();

        await Run(
            t =>
                t.Switch<string, Lane>(s =>
                        s.When(Lane.Left, l => l, requireConfidence: 0.8)
                            .When(Lane.Right, r => r)
                            .Otherwise(o => o)
                            .Shadow<IShadow>()
                    )
                    .Chain<StringToBool>(),
            Shadowed(
                new ScriptedDecider().Choose(Lane.Left, confidence: live),
                new ScriptedDecider().Choose(Lane.Left, confidence: shadow),
                observer
            )
        );

        observer.Decisions.Single().Shadows.Single().Agrees.Should().Be(agrees);
    }

    [Test]
    public async Task ShadowAgreement_OnAGateWithNoUnsureTrack_InTheBand_IsNotAgreement()
    {
        var observer = new RecordingObserver();

        var result = await Run(
            t =>
                t.Gate<string, Flag>(g =>
                        g.Yes(y => y, atLeast: 0.7).No(n => n, below: 0.3).Shadow<IShadow>()
                    )
                    .Chain<StringToBool>(),
            Shadowed(
                new ScriptedDecider().YesNo<Flag>(0.5),
                new ScriptedDecider().YesNo<Flag>(0.6),
                observer
            )
        );

        result.IsLeft.Should().BeTrue("the live answer has no track to take");
        observer
            .Decisions.Single()
            .Shadows.Single()
            .Agrees.Should()
            .BeFalse("both answers failing the run is not agreeing on a track");
    }

    [Test]
    public async Task ShadowAgreement_OnASwitch_ChoosingAMemberWithNoTrackAndNoOtherwise_IsNotAgreement()
    {
        var observer = new RecordingObserver();

        var result = await Run(
            t =>
                t.Switch<string, Lane>(s => s.When(Lane.Left, l => l).Shadow<IShadow>())
                    .Chain<StringToBool>(),
            Shadowed(
                new ScriptedDecider().Choose(Lane.Right),
                new ScriptedDecider().Choose(Lane.Right),
                observer
            )
        );

        result.IsLeft.Should().BeTrue("Right has no track and there is no Otherwise");
        observer.Decisions.Single().Shadows.Single().Agrees.Should().BeFalse();
    }

    [Test]
    public async Task ShadowAgreement_ForAPlainDecide_ComparesTheAnswers()
    {
        var observer = new RecordingObserver();

        await Run(
            t =>
                t.Decide<string>(q => q.YesNo<Flag>().Shadow<IShadow>())
                    .Gate<Flag>(g => g.Yes(y => y, atLeast: 0.7).No(n => n).Unsure(u => u))
                    .Chain<StringToBool>(),
            Shadowed(
                new ScriptedDecider().YesNo<Flag>(0.55),
                new ScriptedDecider().YesNo<Flag>(0.45),
                observer
            )
        );

        observer
            .Decisions.Single()
            .Shadows.Single()
            .Agrees.Should()
            .BeFalse("a Decide cannot see the Gate after it, so it compares sides of one half");
    }

    [Test]
    public async Task ShadowAgreement_AShadowAnswerThatDoesNotFit_NeverAgrees()
    {
        var observer = new RecordingObserver();

        await Run(
            t =>
                t.Switch<string, Lane>(s =>
                        s.When(Lane.Left, l => l).Otherwise(o => o).Shadow<IShadow>()
                    )
                    .Chain<StringToBool>(),
            Shadowed(
                new ScriptedDecider().Choose(Lane.Left),
                new ScriptedDecider().Answer(
                    QuestionKey.For<Lane>(),
                    _ => new ChoiceAnswer("Banana")
                ),
                observer
            )
        );

        observer.Decisions.Single().Shadows.Single().Agrees.Should().BeFalse();
    }

    [Test]
    public async Task Shadow_OnARoutingStepThatAsksNothing_IsRefused() =>
        Failure(
            await Run(
                t =>
                    t.Decide<string>(q => q.Choice<Lane>())
                        .Switch<Lane>(s => Lanes(s, []).Shadow<IShadow>()),
                Shadowed(
                    new ScriptedDecider().Choose(Lane.Left),
                    new ScriptedDecider(),
                    new RecordingObserver()
                )
            )
        )
            .Message.Should()
            .Contain("routes on a decision made earlier and asks nothing to compare");

    #endregion

    #region Replays about another state

    [TestCaseSource(nameof(EveryKindOfAsking))]
    public async Task Replay_OfTheSameState_IsReplayedWithoutAskingTheDecider(
        Func<MonadTask<string, bool>, MonadTask<string, bool>> chain,
        string key,
        ScriptedDecider recording,
        ScriptedDecider live
    )
    {
        var journal = await Recorded(chain, new Services(), recording, input: "20");
        var observer = new RecordingObserver();

        var result = await Run(
            chain,
            new Services()
                .With<IDecider>(live)
                .With<IDecisionReplay>(journal)
                .With<IDecisionObserver>(observer),
            input: "20"
        );

        result.IsRight.Should().BeTrue();
        live.Requests.Should()
            .BeEmpty(
                "0004-a-recorded-answer-replays-only-into-the-same-state.md: the state is the one the recorded answer was given about"
            );

        var made = observer.Decisions.Single(d => d.Question.Key == key);
        made.Replayed.Should().BeTrue();
        made.ReplayRefused.Should().BeNull();
        made.StateHash.Should().Be(journal.StateHash(key));
    }

    [TestCaseSource(nameof(EveryKindOfAsking))]
    public async Task Replay_OfADifferentState_IsAskedAfreshAndSaysWhy(
        Func<MonadTask<string, bool>, MonadTask<string, bool>> chain,
        string key,
        ScriptedDecider recording,
        ScriptedDecider live
    )
    {
        // The answer was given about an amount of 20; the retry is asked about 2000.
        var journal = await Recorded(chain, new Services(), recording, input: "20");
        var observer = new RecordingObserver();

        var result = await Run(
            chain,
            new Services()
                .With<IDecider>(live)
                .With<IDecisionReplay>(journal)
                .With<IDecisionObserver>(observer),
            input: "2000"
        );

        result.IsRight.Should().BeTrue();
        live.Requests.Should()
            .ContainSingle(
                "0004-a-recorded-answer-replays-only-into-the-same-state.md: an answer about 20 is not an answer about 2000"
            )
            .Which.State.Should()
            .Be("2000");

        var made = observer.Decisions.Single(d => d.Question.Key == key);
        made.Replayed.Should().BeFalse();
        made.Decider.Should().Be(typeof(ScriptedDecider));
        made.ReplayRefused.Should().Contain("was given about a different state");
        made.StateHash.Should().NotBeNull().And.NotBe(journal.StateHash(key));
    }

    private static IEnumerable<TestCaseData> EveryKindOfAsking()
    {
        yield return new TestCaseData(
            (Func<MonadTask<string, bool>, MonadTask<string, bool>>)(
                t => t.Decide<string>(q => q.Choice<Lane>()).Switch<Lane>(s => Lanes(s, []))
            ),
            QuestionKey.For<Lane>(),
            new ScriptedDecider().Choose(Lane.Right),
            new ScriptedDecider().Choose(Lane.Left)
        ).SetArgDisplayNames("Decide");
        yield return new TestCaseData(
            (Func<MonadTask<string, bool>, MonadTask<string, bool>>)(
                t => t.Switch<string, Lane>(s => Lanes(s, []))
            ),
            QuestionKey.For<Lane>(),
            new ScriptedDecider().Choose(Lane.Right),
            new ScriptedDecider().Choose(Lane.Left)
        ).SetArgDisplayNames("Switch");
        yield return new TestCaseData(
            (Func<MonadTask<string, bool>, MonadTask<string, bool>>)(
                t =>
                    t.Gate<string, Flag>(g =>
                            g.Yes(y => y, atLeast: 0.7).No(n => n, below: 0.3).Unsure(u => u)
                        )
                        .Chain<StringToBool>()
            ),
            QuestionKey.For<Flag>(),
            new ScriptedDecider().YesNo<Flag>(0.9),
            new ScriptedDecider().YesNo<Flag>(0.1)
        ).SetArgDisplayNames("Gate");
        yield return new TestCaseData(
            (Func<MonadTask<string, bool>, MonadTask<string, bool>>)(
                t => t.Scale<string, Level>(s => s.AtLeast(Level.Low, l => l)).Chain<StringToBool>()
            ),
            QuestionKey.For<Level>(),
            new ScriptedDecider().Score<Level>(2),
            new ScriptedDecider().Score<Level>(0)
        ).SetArgDisplayNames("Scale");
    }

    [Test]
    public async Task Replay_InALoopWhoseItemsComeBackInAnotherOrder_AsksAfreshForEachItemThatMoved()
    {
        // The earlier run asked about 'a' then 'b'. The retry meets them as 'b' then 'a', so the
        // answer recorded for each occurrence was given about the other item.
        var journal = await Recorded(
            TwoItems("a", "b", []),
            new Services(),
            new ScriptedDecider().Choose(Lane.Right)
        );
        var decider = new ScriptedDecider().Choose(Lane.Left);
        var observer = new RecordingObserver();
        var log = new List<string>();

        var result = await Run(
            TwoItems("b", "a", log),
            new Services()
                .With<IDecider>(decider)
                .With<IDecisionReplay>(journal)
                .With<IDecisionObserver>(observer)
        );

        result.IsRight.Should().BeTrue();
        log.Should().Equal("Left", "Left");
        decider
            .Requests.Select(r => r.State)
            .Should()
            .Equal(
                ["b", "a"],
                "0004-a-recorded-answer-replays-only-into-the-same-state.md: each occurrence's answer was about the other item"
            );
        observer
            .Decisions.Should()
            .OnlyContain(d =>
                !d.Replayed && d.ReplayRefused!.Contains("was given about a different state")
            );
    }

    [Test]
    public async Task Replay_InALoop_ReplaysTheItemsThatMatchAndAsksAboutTheOnesThatDoNot()
    {
        var journal = await Recorded(
            TwoItems("a", "b", []),
            new Services(),
            new ScriptedDecider().Choose(Lane.Right)
        );
        var decider = new ScriptedDecider().Choose(Lane.Left);
        var observer = new RecordingObserver();
        var log = new List<string>();

        var result = await Run(
            TwoItems("a", "c", log),
            new Services()
                .With<IDecider>(decider)
                .With<IDecisionReplay>(journal)
                .With<IDecisionObserver>(observer)
        );

        result.IsRight.Should().BeTrue();
        log.Should().Equal("Right", "Left");
        decider
            .Requests.Should()
            .ContainSingle(
                "0004-a-recorded-answer-replays-only-into-the-same-state.md: only the item whose state changed is asked"
            )
            .Which.State.Should()
            .Be("c");
        observer
            .Decisions.Select(d => (d.Occurrence, d.Replayed))
            .Should()
            .Equal((0, true), (1, false));
    }

    [Test]
    public async Task Replay_OfAnAnswerRecordedWithoutAStateHash_IsAskedAfresh()
    {
        var journal = await Recorded(
            t => t.Switch<string, Lane>(s => Lanes(s, [])),
            new Services(),
            new ScriptedDecider().Choose(Lane.Right)
        );
        var decider = new ScriptedDecider().Choose(Lane.Left);
        var observer = new RecordingObserver();
        var log = new List<string>();

        var result = await Run(
            t => t.Switch<string, Lane>(s => Lanes(s, log)),
            new Services()
                .With<IDecider>(decider)
                .With<IDecisionReplay>(journal.HoldStateHash(QuestionKey.For<Lane>(), null))
                .With<IDecisionObserver>(observer)
        );

        result.IsRight.Should().BeTrue();
        log.Should().Equal("Left");
        decider
            .Requests.Should()
            .ContainSingle(
                "0004-a-recorded-answer-replays-only-into-the-same-state.md: an answer recorded without a state hash is not replayed"
            );
        observer
            .Decisions.Single()
            .Should()
            .Match<DecisionMade>(d =>
                !d.Replayed && d.ReplayRefused!.Contains("recorded without a hash of the state")
            );
    }

    [Test]
    public async Task Replay_OfAStateThatCannotBeHashed_IsAskedAfreshWithoutFailing()
    {
        Func<List<string>, Func<MonadTask<string, bool>, MonadTask<string, bool>>> chain = log =>
            t =>
                t.Chain(new MakeParcel(new CallbackParcel()))
                    .Decide<IParcel>(q => q.Choice<Lane>())
                    .Switch<Lane>(s => Lanes(s, log));
        var journal = await Recorded(
            chain([]),
            new Services(),
            new ScriptedDecider().Choose(Lane.Right)
        );
        journal
            .StateHash(QuestionKey.For<Lane>())
            .Should()
            .BeNull("a state holding a delegate has no hash");

        // Even a recorded hash, which a state that cannot be hashed never has, is not trusted
        // against a state that cannot be hashed to compare it with.
        journal.HoldStateHash(QuestionKey.For<Lane>(), new string('0', 64));
        var decider = new ScriptedDecider().Choose(Lane.Left);
        var observer = new RecordingObserver();
        var log = new List<string>();

        var result = await Run(
            chain(log),
            new Services()
                .With<IDecider>(decider)
                .With<IDecisionReplay>(journal)
                .With<IDecisionObserver>(observer)
        );

        result
            .IsRight.Should()
            .BeTrue(
                "0004-a-recorded-answer-replays-only-into-the-same-state.md: a state that cannot be hashed never fails the run"
            );
        log.Should().Equal("Left");
        decider
            .Requests.Should()
            .ContainSingle(
                "0004-a-recorded-answer-replays-only-into-the-same-state.md: a state that cannot be hashed cannot be shown to match"
            );

        var made = observer.Decisions.Single();
        made.Replayed.Should().BeFalse();
        made.StateHash.Should().BeNull();
        made.ReplayRefused.Should().Contain("the state cannot be hashed");
    }

    [Test]
    public async Task StateHash_IsAHashOfTheState_NotTheState()
    {
        var observer = new RecordingObserver();

        await Run(
            t => t.Switch<string, Lane>(s => Lanes(s, [])),
            new Services()
                .With<IDecider>(new ScriptedDecider().Choose(Lane.Left))
                .With<IDecisionObserver>(observer),
            input: "refund 2000"
        );

        observer
            .Decisions.Single()
            .StateHash.Should()
            .MatchRegex("^s1:[0-9a-f]{64}$")
            .And.NotContain("refund");
    }

    [Test]
    public async Task StateType_IsTheRuntimeTypeOfTheStateAskedAbout()
    {
        var observer = new RecordingObserver();

        await Run(
            t =>
                t.Chain(new MakeParcel(new WrappingParcel()))
                    .Decide<IParcel>(q => q.Choice<Lane>())
                    .Switch<Lane>(s => Lanes(s, [])),
            new Services()
                .With<IDecider>(new ScriptedDecider().Choose(Lane.Left))
                .With<IDecisionObserver>(observer)
        );

        observer.Decisions.Single().StateType.Should().Be(typeof(WrappingParcel));
    }

    [Test]
    public async Task StateHash_UnderAKeyFromTheContainer_IsKeyedAndReplays()
    {
        var key = new StateHashKey([.. Enumerable.Range(0, 32).Select(i => (byte)i)]);
        Func<MonadTask<string, bool>, MonadTask<string, bool>> chain = t =>
            t.Switch<string, Lane>(s => Lanes(s, []));
        var journal = await Recorded(
            chain,
            new Services().With(key),
            new ScriptedDecider().Choose(Lane.Right)
        );
        journal
            .StateHash(QuestionKey.For<Lane>())
            .Should()
            .MatchRegex(
                "^k1:[0-9a-f]{64}$",
                "0004-a-recorded-answer-replays-only-into-the-same-state.md: a host that supplies a key gets a keyed hash"
            );

        var decider = new ScriptedDecider().Choose(Lane.Left);
        var observer = new RecordingObserver();

        await Run(
            chain,
            new Services()
                .With(key)
                .With<IDecider>(decider)
                .With<IDecisionReplay>(journal)
                .With<IDecisionObserver>(observer)
        );

        decider.Requests.Should().BeEmpty("the same state under the same key replays");
        observer.Decisions.Single().Replayed.Should().BeTrue();
    }

    [Test]
    public async Task Replay_OfAnAnswerRecordedUnderAKey_IntoARunWithoutOne_IsAskedAfresh()
    {
        Func<MonadTask<string, bool>, MonadTask<string, bool>> chain = t =>
            t.Switch<string, Lane>(s => Lanes(s, []));
        var journal = await Recorded(
            chain,
            new Services().With(new StateHashKey(new byte[32])),
            new ScriptedDecider().Choose(Lane.Right)
        );
        var decider = new ScriptedDecider().Choose(Lane.Left);
        var observer = new RecordingObserver();

        await Run(
            chain,
            new Services()
                .With<IDecider>(decider)
                .With<IDecisionReplay>(journal)
                .With<IDecisionObserver>(observer)
        );

        decider
            .Requests.Should()
            .ContainSingle(
                "0004-a-recorded-answer-replays-only-into-the-same-state.md: a keyed and an unkeyed hash never match"
            );
        observer
            .Decisions.Single()
            .ReplayRefused.Should()
            .Contain(
                "was recorded under a state hash key and this run hashes the state without one",
                "the reason names the key, not a change in the state"
            );
    }

    /// <summary>
    /// Recorded without a key, so a run that fell back to hashing without one when its key could
    /// not be resolved would match the recording and replay it.
    /// </summary>
    [Test]
    public async Task Replay_WhenTheStateHashKeyCannotBeResolved_IsAskedAfresh()
    {
        Func<MonadTask<string, bool>, MonadTask<string, bool>> chain = t =>
            t.Switch<string, Lane>(s => Lanes(s, []));
        var journal = await Recorded(
            chain,
            new Services(),
            new ScriptedDecider().Choose(Lane.Right)
        );
        var decider = new ScriptedDecider().Choose(Lane.Left);
        var observer = new RecordingObserver();

        var result = await Run(
            chain,
            new Services()
                .Throwing<StateHashKey>(new InvalidOperationException("the vault is down"))
                .With<IDecider>(decider)
                .With<IDecisionReplay>(journal)
                .With<IDecisionObserver>(observer)
        );

        result.IsRight.Should().BeTrue("a key that cannot be resolved never fails the run");
        decider
            .Requests.Should()
            .ContainSingle(
                "0004-a-recorded-answer-replays-only-into-the-same-state.md: a state that was not "
                    + "hashed under the host's key cannot be shown to match"
            );
        var made = observer.Decisions.Single();
        made.Replayed.Should().BeFalse();
        made.StateHash.Should().BeNull("the state is not hashed without the key the host meant");
        made.ReplayRefused.Should().Contain("the state hash key could not be resolved");
    }

    [Test]
    public async Task Replay_OfAnAnswerRecordedWithoutAKey_IntoARunWithOne_SaysSo()
    {
        Func<MonadTask<string, bool>, MonadTask<string, bool>> chain = t =>
            t.Switch<string, Lane>(s => Lanes(s, []));
        var journal = await Recorded(
            chain,
            new Services(),
            new ScriptedDecider().Choose(Lane.Right)
        );
        var decider = new ScriptedDecider().Choose(Lane.Left);
        var observer = new RecordingObserver();

        await Run(
            chain,
            new Services()
                .With(new StateHashKey(new byte[32]))
                .With<IDecider>(decider)
                .With<IDecisionReplay>(journal)
                .With<IDecisionObserver>(observer)
        );

        decider.Requests.Should().ContainSingle();
        observer
            .Decisions.Single()
            .ReplayRefused.Should()
            .Contain(
                "was recorded without a state hash key and this run hashes the state under one"
            );
    }

    [Test]
    public async Task Replay_OfAnAnswerRecordedUnderAnotherKey_SaysTheKeyMayHaveChanged()
    {
        Func<MonadTask<string, bool>, MonadTask<string, bool>> chain = t =>
            t.Switch<string, Lane>(s => Lanes(s, []));
        var journal = await Recorded(
            chain,
            new Services().With(new StateHashKey(new byte[32])),
            new ScriptedDecider().Choose(Lane.Right)
        );
        var decider = new ScriptedDecider().Choose(Lane.Left);
        var observer = new RecordingObserver();

        await Run(
            chain,
            new Services()
                .With(new StateHashKey([.. Enumerable.Repeat((byte)1, 32)]))
                .With<IDecider>(decider)
                .With<IDecisionReplay>(journal)
                .With<IDecisionObserver>(observer)
        );

        decider
            .Requests.Should()
            .ContainSingle(
                "0004-a-recorded-answer-replays-only-into-the-same-state.md: a hash under another key never matches"
            );
        observer
            .Decisions.Single()
            .ReplayRefused.Should()
            .Contain("was given about a different state")
            .And.Contain("or the state hash key has changed since");
    }

    [Test]
    public async Task Shadow_OnATupleState_IsHandedTheTuplesItems()
    {
        var shadow = new ScriptedDecider().Choose(Lane.Left);
        var observer = new RecordingObserver();

        var result = await Run(
            t =>
                t.Chain(new Makes<(Order, Customer)>((new Order(20), new Customer("ann"))))
                    .Decide<(Order, Customer)>(q =>
                        q.Choice<Lane>().Shadow<IShadow>().WaitForShadows(Hang)
                    )
                    .Switch<Lane>(s => Lanes(s, [])),
            Shadowed(new ScriptedDecider().Choose(Lane.Left), shadow, observer)
        );

        result.IsRight.Should().BeTrue();
        shadow
            .Requests.Should()
            .ContainSingle()
            .Which.State.Should()
            .Be((new Order(20), new Customer("ann")), "a shadow sees what the live decider sees");
    }

    [TestCaseSource(nameof(ShapesJsonWouldNotTellApart))]
    public async Task Replay_OfAStateThatDiffersWhereJsonWouldNotSee_IsAskedAfresh(
        Func<decimal, decimal, Task<(int Asked, DecisionMade Made)>> repeat
    )
    {
        var (asked, made) = await repeat(20, 2000);

        asked
            .Should()
            .Be(
                1,
                "0004-a-recorded-answer-replays-only-into-the-same-state.md: the decider can "
                    + "read the amount, so an answer about 20 is not one about 2000"
            );
        made.Replayed.Should().BeFalse();
        made.ReplayRefused.Should().Contain("was given about a different state");
    }

    [TestCaseSource(nameof(ShapesJsonWouldNotTellApart))]
    public async Task Replay_OfTheSameValueInEachShape_IsReplayed(
        Func<decimal, decimal, Task<(int Asked, DecisionMade Made)>> repeat
    )
    {
        var (asked, made) = await repeat(20, 20);

        asked
            .Should()
            .Be(
                0,
                "0004-a-recorded-answer-replays-only-into-the-same-state.md: an equal state "
                    + "built again replays"
            );
        made.Replayed.Should().BeTrue();
    }

    private static IEnumerable<TestCaseData> ShapesJsonWouldNotTellApart()
    {
        yield return Shape(
            "ATuple",
            (a, b) =>
                Repeat((new Order(a), new Customer("ann")), (new Order(b), new Customer("ann")))
        );
        yield return Shape(
            "APublicField",
            (a, b) => Repeat(new FieldOrder { Amount = a }, new FieldOrder { Amount = b })
        );
        yield return Shape(
            "ADerivedTypeHeldAsItsAbstractBase",
            (a, b) =>
                Repeat(
                    new Checkout { Payment = new Card { Amount = a } },
                    new Checkout { Payment = new Card { Amount = b } }
                )
        );
        yield return Shape(
            "AJsonIgnoredProperty",
            (a, b) => Repeat(new IgnoredOrder { Amount = a }, new IgnoredOrder { Amount = b })
        );
        yield return Shape(
            "APrivateField",
            (a, b) => Repeat(new PrivateOrder(a), new PrivateOrder(b))
        );
    }

    private static TestCaseData Shape(
        string name,
        Func<decimal, decimal, Task<(int Asked, DecisionMade Made)>> repeat
    ) => new TestCaseData(repeat).SetArgDisplayNames(name);

    /// <summary>
    /// Records a run that asks about <paramref name="first"/>, then repeats it about
    /// <paramref name="second"/>, and says how many times the repeat asked its decider.
    /// </summary>
    private static async Task<(int Asked, DecisionMade Made)> Repeat<T>(T first, T second)
    {
        static Func<MonadTask<string, bool>, MonadTask<string, bool>> About(T state) =>
            t => t.Chain(new Makes<T>(state)).Switch<T, Lane>(s => Lanes(s, []));

        var journal = await Recorded(
            About(first),
            new Services(),
            new ScriptedDecider().Choose(Lane.Right)
        );
        var decider = new ScriptedDecider().Choose(Lane.Left);
        var observer = new RecordingObserver();

        var result = await Run(
            About(second),
            new Services()
                .With<IDecider>(decider)
                .With<IDecisionReplay>(journal)
                .With<IDecisionObserver>(observer)
        );

        result.IsRight.Should().BeTrue();
        return (decider.Requests.Count, observer.Decisions.Single());
    }

    [Test]
    public async Task Replay_OfAStateWithACycle_IsAskedAfreshWithoutFailing()
    {
        static Node Looped()
        {
            var node = new Node();
            node.Next = node;
            return node;
        }

        var (asked, made) = await Repeat(Looped(), Looped());

        asked.Should().Be(1, "0004-a-recorded-answer-replays-only-into-the-same-state.md");
        made.StateHash.Should().BeNull();
        made.ReplayRefused.Should().Contain("recorded without a hash");
    }

    [Test]
    public async Task QuestionType_IsTheTypeTheQuestionWasAskedAbout()
    {
        var observer = new RecordingObserver();

        await Run(
            t =>
                t.Decide<string>(q => q.Choice<Lane>().YesNo<Flag>())
                    .Switch<Lane>(s => Lanes(s, [])),
            new Services()
                .With<IDecider>(new ScriptedDecider().Choose(Lane.Left).YesNo<Flag>(0.9))
                .With<IDecisionObserver>(observer)
        );

        observer.Decisions.Select(d => d.QuestionType).Should().Equal(typeof(Lane), typeof(Flag));
    }

    [Test]
    public async Task QuestionType_IsOnARefusalToo()
    {
        var observer = new RecordingObserver();

        await Run(
            t => t.Switch<string, Lane>(s => Lanes(s, [])),
            new Services().With<IDecider>(new ScriptedDecider()).With<IDecisionObserver>(observer)
        );

        observer.Refusals.Should().ContainSingle().Which.QuestionType.Should().Be(typeof(Lane));
    }

    public sealed record Order(decimal Amount);

    public sealed record Customer(string Name);

    public sealed class FieldOrder
    {
        public decimal Amount;
    }

    public abstract class Payment
    {
        public string Kind { get; set; } = "x";
    }

    public sealed class Card : Payment
    {
        public decimal Amount { get; set; }
    }

    public sealed class Checkout
    {
        public Payment Payment { get; set; } = null!;
    }

    public sealed class IgnoredOrder
    {
        [System.Text.Json.Serialization.JsonIgnore]
        public decimal Amount { get; set; }
    }

    public sealed class PrivateOrder(decimal amount)
    {
        private readonly decimal _amount = amount;

        public bool IsLarge() => _amount > 100;
    }

    public sealed class Node
    {
        public Node? Next { get; set; }
    }

    private sealed class Makes<T>(T value) : Junction<string, T>
    {
        public override Task<T> Run(string input) => Task.FromResult(value);
    }

    /// <summary>
    /// Asks about the lane for <paramref name="first"/> and then for <paramref name="second"/>, as
    /// a loop over two items does: the same question, at occurrences 0 and 1.
    /// </summary>
    private static Func<MonadTask<string, bool>, MonadTask<string, bool>> TwoItems(
        string first,
        string second,
        List<string> log
    ) =>
        t =>
            t.Chain(new Becomes(first))
                .Switch<string, Lane>(s => LaneMarks(s, log))
                .Chain(new Becomes(second))
                .Switch<string, Lane>(s => LaneMarks(s, log))
                .Chain<StringToBool>();

    /// <summary>A Left and a Right track that each note that they ran and carry the state on.</summary>
    private static Tracks<string, bool, Lane> LaneMarks(
        Tracks<string, bool, Lane> tracks,
        List<string> log
    ) =>
        tracks
            .When(Lane.Left, l => l.Chain(new Mark(log, "Left")))
            .When(Lane.Right, r => r.Chain(new Mark(log, "Right")));

    private class Becomes(string value) : Junction<string, string>
    {
        public override Task<string> Run(string input) => Task.FromResult(value);
    }

    #endregion

    #region Replays that no longer fit

    [TestCaseSource(nameof(StaleReplays))]
    public async Task Replay_ThatNoLongerFits_IsAskedAfresh(Answer recorded, string why)
    {
        var decider = new ScriptedDecider().Choose(Lane.Left);
        var observer = new RecordingObserver();
        var log = new List<string>();
        var journal = await Recorded(
            LeftOrOtherwise([]),
            new Services(),
            new ScriptedDecider().Choose(Lane.Left)
        );

        var result = await Run(
            LeftOrOtherwise(log),
            new Services()
                .With<IDecider>(decider)
                .With<IDecisionReplay>(journal.Hold(QuestionKey.For<Lane>(), recorded))
                .With<IDecisionObserver>(observer)
        );

        result.IsRight.Should().BeTrue();
        log.Should().Equal("Left");
        decider.Requests.Should().ContainSingle();

        var made = observer.Decisions.Should().ContainSingle().Subject;
        made.Replayed.Should().BeFalse();
        made.Decider.Should().Be(typeof(ScriptedDecider));
        made.ReplayRefused.Should().Contain("no longer fits").And.Contain(why);
        made.ReplayRefused.Should().NotContain("the decider");
        made.ReplayRefused.Should()
            .NotContain("Middle", "the reason never quotes the recorded answer")
            .And.NotContain("0.9", "the reason never quotes the recorded answer");
    }

    private static IEnumerable<TestCaseData> StaleReplays()
    {
        yield return new TestCaseData(
            new ChoiceAnswer("Middle"),
            "its recorded answer is not one of its options"
        ).SetName("Replay_AskedAfresh_AnOptionSinceRemoved");
        yield return new TestCaseData(new YesNoAnswer(0.9), "not a choice").SetName(
            "Replay_AskedAfresh_AnotherKindOfAnswer"
        );
    }

    [Test]
    public async Task Replay_AMemberTheStepHasNoTrackFor_IsReplayedAndTakesOtherwiseAgain()
    {
        var decider = new ScriptedDecider().Choose(Lane.Left);
        var observer = new RecordingObserver();
        var log = new List<string>();
        var journal = await Recorded(
            LeftOrOtherwise([]),
            new Services(),
            new ScriptedDecider().Choose(Lane.Right)
        );

        var result = await Run(
            LeftOrOtherwise(log),
            new Services()
                .With<IDecider>(decider)
                .With<IDecisionReplay>(journal)
                .With<IDecisionObserver>(observer)
        );

        result.IsRight.Should().BeTrue();
        log.Should()
            .Equal(["Otherwise"], "the earlier run's answer took Otherwise, so this one does");
        decider.Requests.Should().BeEmpty("a requeue repeats the earlier run, not a new decision");
        observer.Decisions.Should().ContainSingle().Which.Replayed.Should().BeTrue();
        observer.Routings.Single().Track.Should().Be("Otherwise");
    }

    [Test]
    public async Task Replay_AScoreOffTheScaleNow_IsAskedAfresh()
    {
        var observer = new RecordingObserver();
        Func<MonadTask<string, bool>, MonadTask<string, bool>> chain = t =>
            t.Scale<string, Level>(s => s.AtLeast(Level.Low, l => l)).Chain<StringToBool>();
        var journal = await Recorded(chain, new Services(), new ScriptedDecider().Score<Level>(2));

        var result = await Run(
            chain,
            new Services()
                .With<IDecider>(new ScriptedDecider().Score<Level>(1))
                .With<IDecisionReplay>(journal.Hold(QuestionKey.For<Level>(), new ScoreAnswer(4)))
                .With<IDecisionObserver>(observer)
        );

        result.IsRight.Should().BeTrue();
        observer
            .Decisions.Single()
            .Should()
            .Match<DecisionMade>(d =>
                !d.Replayed && d.ReplayRefused!.Contains("outside its levels 0 to 2")
            );
    }

    [Test]
    public async Task Replay_AfterAnotherAskingOfTheSameQuestionIsInsertedAheadOfIt_IsAskedAfresh()
    {
        // The earlier run asked about the lane once, in the Switch. The chain now asks about it
        // first in a Decide, so the answer recorded for the first asking was given to another
        // step, and replaying it there would act on a decision about something else.
        var journal = await Recorded(
            t => t.Switch<string, Lane>(s => Lanes(s, [])),
            new Services(),
            new ScriptedDecider().Choose(Lane.Right)
        );
        var decider = new ScriptedDecider().Choose(Lane.Left);
        var observer = new RecordingObserver();

        var result = await Run(
            t => t.Decide<string>(q => q.Choice<Lane>()).Switch<string, Lane>(s => Lanes(s, [])),
            new Services()
                .With<IDecider>(decider)
                .With<IDecisionReplay>(journal)
                .With<IDecisionObserver>(observer)
        );

        result.IsRight.Should().BeTrue();
        decider.Requests.Should().HaveCount(2, "neither asking matches what was recorded");

        var first = observer.Decisions[0];
        first.Replayed.Should().BeFalse();
        first.ReplayRefused.Should().Contain("an earlier version of the chain asked it");
        ((ChoiceAnswer)first.Answer).Choice.Should().Be("Left");
    }

    [Test]
    public async Task Replay_OfAQuestionSinceReworded_IsAskedAfresh()
    {
        var journal = await Recorded(
            t => t.Switch<string, Lane>(s => Lanes(s, []), asking: "Which lane is open?"),
            new Services(),
            new ScriptedDecider().Choose(Lane.Right)
        );
        var observer = new RecordingObserver();
        var log = new List<string>();

        var result = await Run(
            t => t.Switch<string, Lane>(s => Lanes(s, log), asking: "Which lane is closed?"),
            new Services()
                .With<IDecider>(new ScriptedDecider().Choose(Lane.Left))
                .With<IDecisionReplay>(journal)
                .With<IDecisionObserver>(observer)
        );

        result.IsRight.Should().BeTrue();
        log.Should().Equal("Left");
        observer
            .Decisions.Single()
            .ReplayRefused.Should()
            .Contain("an earlier version of the chain asked it");
    }

    [Test]
    public async Task Replay_TheFingerprintIsTheSameOnEveryRunOfAnUnchangedChain()
    {
        var first = new RecordingObserver();
        var second = new RecordingObserver();

        await Run(
            t => t.Switch<string, Lane>(s => Lanes(s, [])),
            new Services()
                .With<IDecider>(new ScriptedDecider().Choose(Lane.Left))
                .With<IDecisionObserver>(first)
        );
        await Run(
            t => t.Switch<string, Lane>(s => Lanes(s, [])),
            new Services()
                .With<IDecider>(new ScriptedDecider().Choose(Lane.Right))
                .With<IDecisionObserver>(second)
        );

        first.Decisions.Single().Fingerprint.Should().NotBeNullOrEmpty();
        second
            .Decisions.Single()
            .Fingerprint.Should()
            .Be(first.Decisions.Single().Fingerprint, "the state and the answer play no part");
    }

    [Test]
    public async Task Replay_IsAskedWithTheRunsCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        var replay = new CountingReplay();

        var train = new DecidingTrain(
            t => t.Switch<string, Lane>(s => Lanes(s, [])),
            new Services()
                .With<IDecider>(new ScriptedDecider().Choose(Lane.Left))
                .With<IDecisionReplay>(replay)
        )
        {
            CancellationToken = cts.Token,
        };

        (await train.RunEither("x")).IsRight.Should().BeTrue();
        replay.Tokens.Should().Equal(cts.Token);
    }

    [Test]
    public async Task Replay_ThatFails_FailsTheStepBeforeTheDeciderIsAsked()
    {
        var decider = new ScriptedDecider().Choose(Lane.Left);

        var failure = Failure(
            await Run(
                t => t.Switch<string, Lane>(s => Lanes(s, [])),
                new Services()
                    .With<IDecider>(decider)
                    .With<IDecisionReplay>(
                        new CountingReplay { Throws = new TimeoutException("journal timed out") }
                    )
            )
        );

        failure.Should().BeOfType<TimeoutException>();
        Data(failure).Junction.Should().Be("Switch<String, Lane>");
        decider.Requests.Should().BeEmpty();
    }

    #endregion

    #region Observers

    [Test]
    public async Task Observer_ThatCannotBeResolved_FailsTheStepBeforeTheDeciderIsAsked()
    {
        var decider = new ScriptedDecider().Choose(Lane.Left);

        var failure = Failure(
            await Run(
                t => t.Switch<string, Lane>(s => Lanes(s, [])),
                new Services()
                    .With<IDecider>(decider)
                    .Throwing<IDecisionObserver>(new InvalidOperationException("bad registration"))
            )
        );

        failure.Message.Should().Be("bad registration");
        Data(failure).Junction.Should().Be("Switch<String, Lane>");
        ClassOf(failure).Should().Be(FailureClass.Transient);
        decider.Requests.Should().BeEmpty();
    }

    [Test]
    public async Task Observer_NotRequired_ThatThrows_DoesNotChangeTheRun()
    {
        var log = new List<string>();

        var result = await Run(
            t => t.Switch<string, Lane>(s => Lanes(s, log)),
            new Services()
                .With<IDecider>(new ScriptedDecider().Choose(Lane.Right))
                .With<IDecisionObserver>(
                    new RecordingObserver { Throws = new IOException("disk full") }
                )
        );

        result.IsRight.Should().BeTrue();
        log.Should().Equal("Right");
    }

    [TestCase(false, FailureClass.Transient)]
    [TestCase(true, FailureClass.Permanent)]
    public async Task Observer_Required_ThatFailsToRecordTheDecision_FailsTheStepBeforeAnyTrack(
        bool classified,
        FailureClass expected
    )
    {
        var thrown = new IOException("journal unavailable");

        if (classified)
            thrown.Data["TrainExceptionData"] = new TrainExceptionData
            {
                TrainName = "elsewhere",
                TrainExternalId = "",
                Junction = "elsewhere",
                Type = nameof(IOException),
                Message = thrown.Message,
                FailureClass = FailureClass.Permanent,
            };

        var log = new List<string>();

        var failure = Failure(
            await Run(
                t => t.Switch<string, Lane>(s => Lanes(s, log)),
                new Services()
                    .With<IDecider>(new ScriptedDecider().Choose(Lane.Left))
                    .With<IDecisionObserver>(
                        new RecordingObserver { IsRequired = true, Throws = thrown }
                    )
            )
        );

        failure.Should().BeSameAs(thrown);
        Data(failure).Junction.Should().Be("Switch<String, Lane>");
        ClassOf(failure).Should().Be(expected);
        log.Should().BeEmpty();
    }

    [Test]
    public async Task Observer_Required_ThatFailsToRecordTheRouting_KeepsTheTrainOffTheTrack()
    {
        var log = new List<string>();

        var failure = Failure(
            await Run(
                t => t.Decide<string>(q => q.Choice<Lane>()).Switch<Lane>(s => Lanes(s, log)),
                new Services()
                    .With<IDecider>(new ScriptedDecider().Choose(Lane.Left))
                    .With<IDecisionObserver>(
                        new RecordingObserver
                        {
                            IsRequired = true,
                            ThrowsOnRouted = new IOException("journal unavailable"),
                        }
                    )
            )
        );

        failure.Message.Should().Be("journal unavailable");
        ClassOf(failure).Should().Be(FailureClass.Transient);
        log.Should().BeEmpty();
    }

    [Test]
    public async Task Observer_IsToldHowManyTimesTheRunAskedTheQuestionBefore()
    {
        var observer = new RecordingObserver();
        var replay = new CountingReplay();

        await Run(
            t =>
                t.Decide<string>(q => q.Choice<Lane>())
                    .Decide<string>(q => q.Choice<Lane>())
                    .Switch<Lane>(s => Lanes(s, [])),
            new Services()
                .With<IDecider>(new ScriptedDecider().Choose(Lane.Left))
                .With<IDecisionReplay>(replay)
                .With<IDecisionObserver>(observer)
        );

        observer.Decisions.Select(d => d.Occurrence).Should().Equal(0, 1);
        replay.Occurrences.Should().Equal(0, 1);
    }

    #endregion

    #region Refused answers

    [Test]
    public async Task Refused_AQuestionLeftUnanswered_IsToldToTheObserverBeforeTheStepFails()
    {
        var observer = new RecordingObserver();
        var log = new List<string>();

        var failure = Failure(
            await Run(
                t => t.Switch<string, Lane>(s => Lanes(s, log)),
                new Services()
                    .With<IDecider>(new ScriptedDecider())
                    .With<IDecisionObserver>(observer)
            )
        );

        var refusal = observer.Refusals.Should().ContainSingle().Subject;
        refusal.Question.Key.Should().Be(QuestionKey.For<Lane>());
        refusal.Answer.Should().BeNull();
        refusal.Decider.Should().Be<ScriptedDecider>();
        refusal.Reason.Should().Be($"the decider gave no answer to '{QuestionKey.For<Lane>()}'");
        refusal.Occurrence.Should().Be(0);
        refusal.Fingerprint.Should().MatchRegex("^[0-9a-f]{64}$");
        refusal.RunId.Should().NotBeNullOrEmpty();
        observer.Decisions.Should().BeEmpty("nothing was decided");
        failure.Message.Should().Contain(refusal.Reason);
        ClassOf(failure).Should().Be(FailureClass.Transient);
        log.Should().BeEmpty();
    }

    [Test]
    public async Task Refused_AnAnswerThatDoesNotFit_IsToldToTheObserverWithTheAnswerAndWhy()
    {
        var observer = new RecordingObserver();
        var banana = new ChoiceAnswer("Banana", 0.9) { Model = "jev-1" };

        var failure = Failure(
            await Run(
                t => t.Switch<string, Lane>(s => Lanes(s, [])),
                new Services()
                    .With<IDecider>(
                        new ScriptedDecider().Answer(QuestionKey.For<Lane>(), _ => banana)
                    )
                    .With<IDecisionObserver>(observer)
            )
        );

        var refusal = observer.Refusals.Should().ContainSingle().Subject;
        refusal.Answer.Should().Be(banana);
        refusal
            .Reason.Should()
            .Be(
                $"the decider answered '{QuestionKey.For<Lane>()}' with 'Banana', which is not "
                    + "one of its options"
            );
        failure.Message.Should().Contain("'Banana', which is not one of its options");
        ClassOf(failure).Should().Be(FailureClass.Transient);
    }

    [Test]
    public async Task Refused_EveryBadAnswerInOneDecide_IsToldAndNamedInTheFailure()
    {
        var observer = new RecordingObserver();

        var failure = Failure(
            await Run(
                t =>
                    t.Decide<string>(q => q.Choice<Lane>().YesNo<Flag>().Score<Level>())
                        .Switch<Lane>(s => Lanes(s, [])),
                new Services()
                    .With<IDecider>(new ScriptedDecider().Choose(Lane.Left).YesNo<Flag>(1.5))
                    .With<IDecisionObserver>(observer)
            )
        );

        observer
            .Refusals.Select(r => (r.Question.Key, r.Answer))
            .Should()
            .Equal(
                (QuestionKey.For<Flag>(), new YesNoAnswer(1.5)),
                (QuestionKey.For<Level>(), (Answer?)null)
            );
        observer.Decisions.Should().BeEmpty("a step with a refused answer acts on none of them");
        failure.Message.Should().Contain("probability of 1.5").And.Contain("gave no answer");
    }

    [Test]
    public async Task Refused_ACascadeThatEscalatesToAnAnswerThatFits_IsNotARefusal()
    {
        var observer = new RecordingObserver();
        var log = new List<string>();
        var cascade = new CascadingDecider(
            new ScriptedDecider().Answer(QuestionKey.For<Lane>(), _ => new ChoiceAnswer("Banana")),
            new ScriptedDecider().Choose(Lane.Right)
        );

        var result = await Run(
            t => t.Switch<string, Lane>(s => Lanes(s, log)),
            new Services().With<IDecider>(cascade).With<IDecisionObserver>(observer)
        );

        result.IsRight.Should().BeTrue();
        log.Should().Equal("Right");
        observer.Refusals.Should().BeEmpty();
        observer.Decisions.Should().ContainSingle();
    }

    [Test]
    public async Task Refused_ARequiredObserverThatCannotRecordIt_LeavesTheRefusalAsTheFailure()
    {
        var failure = Failure(
            await Run(
                t => t.Switch<string, Lane>(s => Lanes(s, [])),
                new Services()
                    .With<IDecider>(new ScriptedDecider())
                    .With<IDecisionObserver>(
                        new RecordingObserver
                        {
                            IsRequired = true,
                            ThrowsOnRefused = new IOException("journal unavailable"),
                        }
                    )
            )
        );

        failure.Message.Should().Contain("gave no answer");
        ClassOf(failure).Should().Be(FailureClass.Transient);
    }

    [Test]
    public async Task Refused_AnObserverThatDoesNotListenForRefusals_StillSeesTheStepFail()
    {
        var failure = Failure(
            await Run(
                t => t.Switch<string, Lane>(s => Lanes(s, [])),
                new Services()
                    .With<IDecider>(new ScriptedDecider())
                    .With<IDecisionObserver>(new DecisionsOnly())
            )
        );

        failure.Message.Should().Contain("gave no answer");
    }

    /// <summary>Implements only what the observer had to before refusals were reported.</summary>
    private sealed class DecisionsOnly : IDecisionObserver
    {
        public Task Decided(DecisionMade decision, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task Routed(TrackRouted routing, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    #endregion

    #region Cancellation

    [Test]
    public async Task Cancelled_BeforeASwitchAsksItsQuestion_AsksNothingTellsNobodyAndTakesNoTrack()
    {
        using var cts = new CancellationTokenSource();
        var decider = new ScriptedDecider().Choose(Lane.Left);
        var observer = new RecordingObserver();
        var log = new List<string>();

        var train = new DecidingTrain(
            t => t.Chain(new CancelsRun(cts)).Switch<string, Lane>(s => Lanes(s, log)),
            new Services().With<IDecider>(decider).With<IDecisionObserver>(observer)
        )
        {
            CancellationToken = cts.Token,
        };

        Failure(await train.RunEither("x")).Should().BeAssignableTo<OperationCanceledException>();
        decider.Requests.Should().BeEmpty();
        observer.Decisions.Should().BeEmpty();
        observer.Routings.Should().BeEmpty();
        log.Should().BeEmpty();
    }

    [Test]
    public async Task Cancelled_BeforeARouting_TellsNobodyAndTakesNoTrack()
    {
        using var cts = new CancellationTokenSource();
        var observer = new RecordingObserver();
        var log = new List<string>();

        var train = new DecidingTrain(
            t =>
                t.Decide<string>(q => q.Choice<Lane>())
                    .Chain(new CancelsRun(cts))
                    .Switch<Lane>(s => Lanes(s, log)),
            new Services()
                .With<IDecider>(new ScriptedDecider().Choose(Lane.Left))
                .With<IDecisionObserver>(observer)
        )
        {
            CancellationToken = cts.Token,
        };

        Failure(await train.RunEither("x")).Should().BeAssignableTo<OperationCanceledException>();
        observer.Decisions.Should().ContainSingle();
        observer.Routings.Should().BeEmpty();
        log.Should().BeEmpty();
    }

    [Test]
    public async Task Cancelled_WhileTheDeciderAnswers_PutsNothingInMemoryAndTellsNobody()
    {
        using var cts = new CancellationTokenSource();
        var observer = new RecordingObserver();

        var train = new DecidingTrain(
            t => t.Switch<string, Lane>(s => Lanes(s, [])),
            new Services()
                .With<IDecider>(new CancellingDecider(cts, new ChoiceAnswer("Left")))
                .With<IDecisionObserver>(observer)
        )
        {
            CancellationToken = cts.Token,
        };

        Failure(await train.RunEither("x")).Should().BeAssignableTo<OperationCanceledException>();
        observer.Decisions.Should().BeEmpty();
    }

    #endregion

    #region Verification

    [Test]
    public void Verify_ADecideFromAMissingState_IsReportedOnceNotOncePerQuestion() =>
        ChainVerification
            .Verify(
                new DecidingTrain(
                    t =>
                        t.Decide<int>(q => q.Choice<Lane>().YesNo<Flag>().Score<Level>())
                            .Chain<StringToBool>(),
                    new Services()
                ).DeclaredChain(),
                typeof(string),
                typeof(bool),
                new Registered(typeof(IDecider))
            )
            .Should()
            .ContainSingle()
            .Which.Reason.Should()
            .Contain("decides from 'System.Int32'");

    [Test]
    public void Verify_ADecideWithNoDecider_IsReportedOnceNotOncePerQuestion() =>
        ChainVerification
            .Verify(
                new DecidingTrain(
                    t =>
                        t.Decide<string>(q => q.Choice<Lane>().YesNo<Flag>().Score<Level>())
                            .Chain<StringToBool>(),
                    new Services()
                ).DeclaredChain(),
                typeof(string),
                typeof(bool),
                new Registered()
            )
            .Should()
            .ContainSingle()
            .Which.Reason.Should()
            .Contain("needs a decider");

    #endregion

    #region Fixtures

    private static Exception Failure(Either<Exception, bool> result) =>
        result.IsLeft
            ? result.Swap().ValueUnsafe()
            : throw new AssertionException("the run did not fail");

    private static TrainExceptionData Data(Exception e) =>
        (TrainExceptionData)e.Data["TrainExceptionData"]!;

    private static FailureClass? ClassOf(Exception e) => Data(e).FailureClass;

    private static Task<Either<Exception, bool>> Run(
        Func<MonadTask<string, bool>, MonadTask<string, bool>> chain,
        Services services,
        string input = "x"
    ) => new DecidingTrain(chain, services).RunEither(input);

    private static Services Shadowed(
        ScriptedDecider live,
        ScriptedDecider shadow,
        IDecisionObserver observer
    ) =>
        new Services()
            .With<IDecider>(live)
            .With<IShadow>(new ScriptedShadow(shadow))
            .With<IDecisionObserver>(observer);

    /// <summary>A Left and a Right track, each noting that it ran, then a value to finish on.</summary>
    private static Tracks<string, bool, Lane> Lanes(
        Tracks<string, bool, Lane> tracks,
        List<string> log
    ) =>
        tracks
            .When(Lane.Left, l => l.Chain(new Mark(log, "Left")).Chain<StringToBool>())
            .When(Lane.Right, r => r.Chain(new Mark(log, "Right")).Chain<StringToBool>());

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

    [Asks("Is the flag up?")]
    public sealed class Flag;

    public interface IShadow : IDecider;

    /// <summary>
    /// A train whose services come from a container, so a test can register anything, including a
    /// registration that throws.
    /// </summary>
    private sealed class DecidingTrain(
        Func<MonadTask<string, bool>, MonadTask<string, bool>> chain,
        IServiceProvider services
    ) : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            chain(AddServices(services).Chain<Start>()).Resolve();
    }

    private sealed class Services : IServiceProvider
    {
        private readonly Dictionary<Type, Func<object>> _registered = [];

        public Services With<T>(T service)
            where T : class
        {
            _registered[typeof(T)] = () => service;
            return this;
        }

        public Services Throwing<T>(Exception failure)
        {
            _registered[typeof(T)] = () => throw failure;
            return this;
        }

        public object? GetService(Type serviceType) =>
            _registered.TryGetValue(serviceType, out var make) ? make() : null;
    }

    /// <summary>Says which services a container holds, for verification.</summary>
    private sealed class Registered(params Type[] types)
        : Microsoft.Extensions.DependencyInjection.IServiceProviderIsService
    {
        public bool IsService(Type serviceType) => types.Contains(serviceType);
    }

    public interface IOtherShadow : IDecider;

    public sealed class Basket
    {
        public string Name { get; set; } = "";

        public List<string> Items { get; } = [];
    }

    public interface IParcel;

    /// <summary>A state JSON cannot write: it holds a delegate.</summary>
    public sealed class CallbackParcel : IParcel
    {
        public Action Notify { get; set; } = () => { };
    }

    /// <summary>A state JSON writes but cannot read back: it holds a member declared as an interface.</summary>
    public sealed class WrappingParcel : IParcel
    {
        public IParcel? Inner { get; set; }

        public sealed class Plain : IParcel;
    }

    private sealed class MakeBasket(Basket basket) : Junction<string, Basket>
    {
        public override Task<Basket> Run(string input) => Task.FromResult(basket);
    }

    private sealed class MakeParcel(IParcel parcel) : Junction<string, IParcel>
    {
        public override Task<IParcel> Run(string input) => Task.FromResult(parcel);
    }

    /// <summary>Notes the state it was handed, and chooses Left.</summary>
    private sealed class SeeingDecider : IDecider
    {
        public object? Seen { get; private set; }

        public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken ct)
        {
            Seen = request.State;
            return Left();
        }

        public static Task<DecisionResult> Left() =>
            Task.FromResult(
                new DecisionResult(
                    new Dictionary<string, Answer>
                    {
                        [QuestionKey.For<Lane>()] = new ChoiceAnswer("Left"),
                    }
                )
            );
    }

    /// <summary>Notes the basket it was handed, then changes it, and chooses Left.</summary>
    private sealed class MutatingShadow(string mark) : IShadow, IOtherShadow
    {
        public Basket? Seen { get; private set; }

        public string? Saw { get; private set; }

        public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken ct)
        {
            var basket = (Basket)request.State;

            lock (basket)
            {
                Seen = basket;
                Saw = $"{basket.Name}: {string.Join(", ", basket.Items)}";
                basket.Name = mark;
                basket.Items.Add(mark);
            }

            return SeeingDecider.Left();
        }
    }

    /// <summary>A scoped service that says when its scope disposed it.</summary>
    private sealed class Tracked : IDisposable
    {
        public TaskCompletionSource Disposed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Dispose() => Disposed.TrySetResult();
    }

    /// <summary>A shadow, holding a scoped service, that ignores cancellation and never returns.</summary>
    private sealed class HangingWith(Tracked tracked) : IShadow
    {
        public Tracked Tracked => tracked;

        public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken ct) =>
            new TaskCompletionSource<DecisionResult>().Task;
    }

    private sealed class HangingShadow : IShadow
    {
        public CancellationToken Token { get; private set; }

        /// <summary>
        /// Completes once the shadow has been asked, which happens on its own thread and may be
        /// after the step has stopped waiting for it.
        /// </summary>
        public TaskCompletionSource Entered { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken ct)
        {
            Token = ct;
            Entered.TrySetResult();
            return new TaskCompletionSource<DecisionResult>().Task;
        }
    }

    /// <summary>A scoped service that refuses to be used by two callers at once, as a DbContext does.</summary>
    private sealed class NotThreadSafe : IDisposable
    {
        private int _users;

        public bool Disposed { get; private set; }

        public IDisposable Enter()
        {
            if (Interlocked.Increment(ref _users) != 1)
            {
                Interlocked.Decrement(ref _users);
                throw new InvalidOperationException("used by two callers at once");
            }

            return new Leave(this);
        }

        public void Dispose() => Disposed = true;

        private sealed class Leave(NotThreadSafe owner) : IDisposable
        {
            public void Dispose() => Interlocked.Decrement(ref owner._users);
        }
    }

    /// <summary>Holds its scoped service until the shadow has tried to use its own.</summary>
    private sealed class HoldingDecider(
        NotThreadSafe resource,
        TaskCompletionSource entered,
        TaskCompletionSource shadowDone
    ) : IDecider
    {
        public async Task<DecisionResult> Decide(DecisionRequest request, CancellationToken ct)
        {
            using (resource.Enter())
            {
                entered.TrySetResult();
                await shadowDone.Task.WaitAsync(Hang, ct);
            }

            return new DecisionResult(
                new Dictionary<string, Answer>
                {
                    [QuestionKey.For<Lane>()] = new ChoiceAnswer("Left"),
                }
            );
        }
    }

    /// <summary>Uses its scoped service while the live decider is holding its own.</summary>
    private sealed class ConcurrentShadow(
        NotThreadSafe resource,
        TaskCompletionSource liveEntered,
        TaskCompletionSource done
    ) : IShadow
    {
        public async Task<DecisionResult> Decide(DecisionRequest request, CancellationToken ct)
        {
            try
            {
                await liveEntered.Task.WaitAsync(Hang, ct);

                using (resource.Enter())
                    return new DecisionResult(
                        new Dictionary<string, Answer>
                        {
                            [QuestionKey.For<Lane>()] = new ChoiceAnswer("Left"),
                        }
                    );
            }
            finally
            {
                done.TrySetResult();
            }
        }
    }

    private sealed class ScriptedShadow(ScriptedDecider inner) : IShadow
    {
        public ScriptedDecider Inner => inner;

        public Task<DecisionResult> Decide(DecisionRequest request, CancellationToken ct) =>
            inner.Decide(request, ct);
    }

    /// <summary>Cancels the run while it is being asked, then answers anyway.</summary>
    private sealed class CancellingDecider(CancellationTokenSource cts, Answer answer) : IDecider
    {
        public async Task<DecisionResult> Decide(DecisionRequest request, CancellationToken ct)
        {
            await cts.CancelAsync();
            return new DecisionResult(request.Questions.ToDictionary(q => q.Key, _ => answer));
        }
    }

    private sealed class RecordingObserver : IDecisionObserver
    {
        public List<DecisionMade> Decisions { get; } = [];

        public List<TrackRouted> Routings { get; } = [];

        public List<DecisionRefused> Refusals { get; } = [];

        public Exception? ThrowsOnRefused { get; init; }

        public bool IsRequired { get; init; }

        public Exception? Throws { get; init; }

        public Exception? ThrowsOnRouted { get; init; }

        public bool Required => IsRequired;

        public Task Decided(DecisionMade decision, CancellationToken cancellationToken)
        {
            if (Throws is not null)
                throw Throws;

            Decisions.Add(decision);
            return Task.CompletedTask;
        }

        public Task Routed(TrackRouted routing, CancellationToken cancellationToken)
        {
            if (ThrowsOnRouted is not null)
                return Task.FromException(ThrowsOnRouted);

            Routings.Add(routing);
            return Task.CompletedTask;
        }

        public Task Refused(DecisionRefused refusal, CancellationToken cancellationToken)
        {
            if (ThrowsOnRefused is not null)
                return Task.FromException(ThrowsOnRefused);

            Refusals.Add(refusal);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Runs <paramref name="chain"/> once with <paramref name="decider"/>, recording what it asked
    /// the way a host's journal does, so a later run of the chain can replay it.
    /// </summary>
    private static async Task<Journal> Recorded(
        Func<MonadTask<string, bool>, MonadTask<string, bool>> chain,
        Services services,
        IDecider decider,
        string input = "x"
    )
    {
        var journal = new Journal();
        (await Run(chain, services.With<IDecider>(decider).With<IDecisionObserver>(journal), input))
            .IsRight.Should()
            .BeTrue("the run being recorded must succeed");
        return journal;
    }

    /// <summary>A Left track, and an Otherwise for anything else, each noting that it ran.</summary>
    private static Func<MonadTask<string, bool>, MonadTask<string, bool>> LeftOrOtherwise(
        List<string> log
    ) =>
        t =>
            t.Switch<string, Lane>(s =>
                    s.When(Lane.Left, l => l.Chain(new Mark(log, "Left")))
                        .Otherwise(o => o.Chain(new Mark(log, "Otherwise")))
                )
                .Chain<StringToBool>();

    /// <summary>
    /// Records every answer with its fingerprint and state hash and replays it by key and
    /// occurrence, as a host's journal does. <see cref="Hold"/> replaces a recorded answer, keeping
    /// the fingerprint and hash, to replay an answer of the test's choosing against the question as
    /// it was really asked. <see cref="HoldStateHash"/> replaces the hash alone.
    /// </summary>
    private sealed class Journal : IDecisionObserver, IDecisionReplay
    {
        private readonly Dictionary<(string Key, int Occurrence), RecordedAnswer> _recorded = [];

        /// <summary>What a test chose to replay, which replaces everything recorded.</summary>
        private Dictionary<(string Key, int Occurrence), RecordedAnswer>? _held;

        public Journal Hold(string key, Answer answer)
        {
            _held ??= [];
            _held[(key, 0)] = _recorded[(key, 0)] with { Answer = answer };
            return this;
        }

        public Journal HoldStateHash(string key, string? stateHash)
        {
            _held ??= new(_recorded);
            _held[(key, 0)] = _recorded[(key, 0)] with { StateHash = stateHash };
            return this;
        }

        public string? StateHash(string key, int occurrence = 0) =>
            _recorded[(key, occurrence)].StateHash;

        public Task Decided(DecisionMade decision, CancellationToken cancellationToken)
        {
            _recorded[(decision.Question.Key, decision.Occurrence)] = new RecordedAnswer(
                decision.Answer,
                decision.Fingerprint
            )
            {
                StateHash = decision.StateHash,
            };
            return Task.CompletedTask;
        }

        public Task Routed(TrackRouted routing, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public async Task<RecordedAnswer?> Replay(
            string train,
            string runId,
            string key,
            int occurrence,
            CancellationToken cancellationToken
        )
        {
            // A host reads its journal from a database, so the run awaits it.
            await Task.Yield();
            return (_held ?? _recorded).GetValueOrDefault((key, occurrence));
        }
    }

    private sealed class CountingReplay : IDecisionReplay
    {
        public List<int> Occurrences { get; } = [];

        public List<CancellationToken> Tokens { get; } = [];

        public Exception? Throws { get; init; }

        public Task<RecordedAnswer?> Replay(
            string train,
            string runId,
            string key,
            int occurrence,
            CancellationToken cancellationToken
        )
        {
            if (Throws is not null)
                return Task.FromException<RecordedAnswer?>(Throws);

            Occurrences.Add(occurrence);
            Tokens.Add(cancellationToken);
            return Task.FromResult<RecordedAnswer?>(null);
        }
    }

    private class Start : Junction<string, string>
    {
        public override Task<string> Run(string input) => Task.FromResult(input);
    }

    private class StringToBool : Junction<string, bool>
    {
        public override Task<bool> Run(string input) => Task.FromResult(true);
    }

    private class Mark(List<string> log, string name) : Junction<string, string>
    {
        public override Task<string> Run(string input)
        {
            log.Add(name);
            return Task.FromResult(input);
        }
    }

    private class CancelsRun(CancellationTokenSource cts) : Junction<string, string>
    {
        public override async Task<string> Run(string input)
        {
            await cts.CancelAsync();
            return input;
        }
    }

    #endregion
}
