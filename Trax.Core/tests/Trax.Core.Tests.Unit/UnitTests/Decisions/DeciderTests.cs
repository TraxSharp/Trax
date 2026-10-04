using AwesomeAssertions;
using Trax.Core.Decisions;
using Trax.Core.Functional;
using Trax.Core.Junction;
using Trax.Core.Train;

namespace Trax.Core.Tests.Unit.UnitTests.Decisions;

/// <summary>
/// The deciders Trax ships, the key a question is asked under, and the order an enum's levels
/// are offered in.
/// </summary>
public class DeciderTests : TestSetup
{
    private static DecisionRequest Request(params Question[] questions) =>
        new("Train", "state", questions);

    private static readonly ChoiceQuestion LaneQuestion = new(
        QuestionKey.For<Lane>(),
        "Which lane?",
        [new Criterion("Left", null), new Criterion("Right", null)]
    );

    private static readonly YesNoQuestion FlagQuestion = new(
        QuestionKey.For<Flag>(),
        "Is the flag up?",
        null,
        null
    );

    #region CascadingDecider

    [Test]
    public async Task Cascade_WhenTheFirstDeciderFails_AsksTheSecondEverything()
    {
        var then = new ScriptedDecider().Choose(Lane.Right).YesNo<Flag>(0.9);
        var cascade = new CascadingDecider(
            new ScriptedDecider().Throws(new HttpRequestException("model endpoint returned 503")),
            then
        );

        var result = await cascade.Decide(Request(LaneQuestion, FlagQuestion), default);

        result.Answers.Keys.Should().BeEquivalentTo(LaneQuestion.Key, FlagQuestion.Key);
        then.Requests.Should().ContainSingle().Which.Questions.Should().HaveCount(2);
    }

    [Test]
    public async Task Cascade_WhenTheCallerCancels_DoesNotFallThrough()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var then = new ScriptedDecider().Choose(Lane.Right);
        var cascade = new CascadingDecider(
            new ScriptedDecider().Throws(new OperationCanceledException(cts.Token)),
            then
        );

        var act = () => cascade.Decide(Request(LaneQuestion), cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
        then.Requests.Should().BeEmpty();
    }

    [Test]
    public async Task Cascade_ANaNConfidence_IsEscalated()
    {
        var then = new ScriptedDecider().Choose(Lane.Right);
        var cascade = new CascadingDecider(
            new ScriptedDecider().Choose(Lane.Left, confidence: double.NaN),
            then
        );

        var result = await cascade.Decide(Request(LaneQuestion), default);

        result.Answers[LaneQuestion.Key].Should().Be(new ChoiceAnswer("Right"));
    }

    [Test]
    public async Task Cascade_ANaNProbability_IsEscalated()
    {
        var then = new ScriptedDecider().YesNo<Flag>(0.05);
        var cascade = new CascadingDecider(new ScriptedDecider().YesNo<Flag>(double.NaN), then);

        var result = await cascade.Decide(Request(FlagQuestion), default);

        result.Answers[FlagQuestion.Key].Should().Be(new YesNoAnswer(0.05));
    }

    [TestCaseSource(nameof(UnfitFirstAnswers))]
    public async Task Cascade_AnAnswerThatDoesNotFitTheQuestion_IsEscalated(
        Question question,
        Answer unfit
    )
    {
        var then = new ScriptedDecider().Choose(Lane.Right).YesNo<Flag>(0.05).Score<Temperature>(1);
        var cascade = new CascadingDecider(
            new ScriptedDecider().Answer(question.Key, _ => unfit),
            then
        );

        var result = await cascade.Decide(Request(question), default);

        then.Requests.Should().ContainSingle("an answer the run would refuse is not a sure one");
        result.Answers[question.Key].Should().NotBe(unfit);
    }

    private static readonly ScoreQuestion WarmthQuestion = new(
        QuestionKey.For<Temperature>(),
        "How warm?",
        [
            new Criterion("Freezing", null),
            new Criterion("Cold", null),
            new Criterion("Mild", null),
            new Criterion("Hot", null),
        ]
    );

    private static IEnumerable<TestCaseData> UnfitFirstAnswers()
    {
        TestCaseData Case(string name, Question question, Answer answer) =>
            new TestCaseData(question, answer).SetName($"Cascade_Escalates_{name}");

        yield return Case("AnOptionItWasNotOffered", LaneQuestion, new ChoiceAnswer("Banana"));
        yield return Case("AConfidenceAboveOne", LaneQuestion, new ChoiceAnswer("Left", 1.5));
        yield return Case(
            "AProbabilityOutsideZeroToOne",
            LaneQuestion,
            new ChoiceAnswer("Left", 1, new Dictionary<string, double> { ["Left"] = 2 })
        );
        yield return Case("AnotherKindOfAnswer", LaneQuestion, new YesNoAnswer(0.99));
        yield return Case("AYesNoAboveOne", FlagQuestion, new YesNoAnswer(1.5));
        yield return Case("AScoreOffTheLevels", WarmthQuestion, new ScoreAnswer(4));
        yield return Case(
            "TooFewLevelProbabilities",
            WarmthQuestion,
            new ScoreAnswer(1, 1, [0.1, 0.9])
        );
    }

    [TestCase(1.5, 0.2, 0.8, "escalateBelow")]
    [TestCase(double.NaN, 0.2, 0.8, "escalateBelow")]
    [TestCase(0.8, -0.1, 0.8, "unsureAbove")]
    [TestCase(0.8, 0.2, 1.1, "unsureBelow")]
    [TestCase(0.8, 0.9, 0.1, "unsureBelow")]
    public void Cascade_ABandThatIsNotAProbabilityOrRunsBackwards_IsRefused(
        double escalateBelow,
        double unsureAbove,
        double unsureBelow,
        string parameter
    )
    {
        var act = () =>
            new CascadingDecider(
                new ScriptedDecider(),
                new ScriptedDecider(),
                escalateBelow,
                unsureAbove,
                unsureBelow
            );

        act.Should().Throw<ArgumentException>().Which.ParamName.Should().Be(parameter);
    }

    #endregion

    #region ScriptedDecider

    [Test]
    public async Task Scripted_AskedFromManyThreadsAtOnce_KeepsEveryRequest()
    {
        var decider = new ScriptedDecider().Choose(Lane.Left);
        const int asked = 2000;

        await Task.WhenAll(
            Enumerable
                .Range(0, asked)
                .Select(_ => Task.Run(() => decider.Decide(Request(LaneQuestion), default)))
        );

        decider.Requests.Should().HaveCount(asked);
    }

    #endregion

    #region Question keys

    [Test]
    public void QuestionKey_TellsApartNestedTypesThatShareAName()
    {
        QuestionKey.For<Billing.Priority>().Should().NotBe(QuestionKey.For<Support.Priority>());
        QuestionKey.For<Wrapper<Flag>>().Should().NotBe(QuestionKey.For<Wrapper<Lane>>());
    }

    [Test]
    public async Task QuestionKey_TwoQuestionsWithTheSameShortName_AreAskedTogether()
    {
        var decider = new ScriptedDecider()
            .Choose(Billing.Priority.High)
            .Choose(Support.Priority.Urgent);

        var result = await new PriorityTrain(decider).RunEither("x");

        result.IsRight.Should().BeTrue();
        decider.Requests.Should().ContainSingle().Which.Questions.Should().HaveCount(2);
    }

    [Test]
    public async Task QuestionKey_RuleDeciderKeepsARulePerKey()
    {
        var rules = new RuleDecider()
            .YesNo<string, Wrapper<Flag>>(_ => true)
            .YesNo<string, Wrapper<Lane>>(_ => false);

        var result = await rules.Decide(
            Request(
                new YesNoQuestion(QuestionKey.For<Wrapper<Flag>>(), "?", null, null),
                new YesNoQuestion(QuestionKey.For<Wrapper<Lane>>(), "?", null, null)
            ),
            default
        );

        result.Answers.Values.Should().Equal(new YesNoAnswer(1.0), new YesNoAnswer(0.0));
    }

    #endregion

    #region Enum order

    [Test]
    public void EnumMembers_OrdersNegativeMembersFirst() =>
        EnumMembers<Temperature>
            .Ordered.Should()
            .Equal(Temperature.Freezing, Temperature.Cold, Temperature.Mild, Temperature.Hot);

    [Test]
    public void EnumMembers_OrdersAnUnsignedEnumAboveTheSignedRange() =>
        EnumMembers<Huge>.Ordered.Should().Equal(Huge.Small, Huge.Large);

    [Test]
    public async Task Scale_OnAnEnumWithNegativeMembers_OffersTheLevelsLowestFirstAndRoutesByThem()
    {
        var decider = new ScriptedDecider().Score<Temperature>(1);

        var result = await new ThermostatTrain(decider).RunEither("x");

        result.IsRight.Should().BeTrue();
        decider
            .Requests.Single()
            .Questions.Single()
            .Should()
            .BeOfType<ScoreQuestion>()
            .Which.Levels.Select(l => l.Name)
            .Should()
            .Equal("Freezing", "Cold", "Mild", "Hot");
        ThermostatTrain
            .Taken.Should()
            .Be("Freezing", "score 1 is Cold, and Freezing's track covers it");
    }

    [Test]
    public async Task RuleDecider_ScoresANegativeMemberByItsPlaceOnTheScale()
    {
        var rules = new RuleDecider().Score<string, Temperature>(_ => Temperature.Cold);

        var result = await rules.Decide(
            Request(new ScoreQuestion(QuestionKey.For<Temperature>(), "?", [])),
            default
        );

        result.Answers.Values.Single().Should().Be(new ScoreAnswer(1));
    }

    #endregion

    #region Fixtures

    /// <summary>Two enums with one name, nested in different types.</summary>
    public static class Billing
    {
        [Asks("How soon must billing act?")]
        public enum Priority
        {
            Low,
            High,
        }
    }

    public static class Support
    {
        [Asks("How soon must support act?")]
        public enum Priority
        {
            Normal,
            Urgent,
        }
    }

    [Asks("Which lane?")]
    public enum Lane
    {
        Left,
        Right,
    }

    [Asks("Is the flag up?")]
    public sealed class Flag;

    [Asks("Is it wrapped?")]
    public sealed class Wrapper<T>;

    [Asks("How warm?")]
    public enum Temperature
    {
        Mild = 0,
        Hot = 1,
        Freezing = -2,
        Cold = -1,
    }

    [Asks("How big?")]
    public enum Huge : ulong
    {
        Large = ulong.MaxValue,
        Small = 1,
    }

    private sealed class PriorityTrain(IDecider decider) : Train<string, bool>
    {
        protected override Task<Either<Exception, bool>> Junctions() =>
            AddServices(decider)
                .Decide<string>(q => q.Choice<Billing.Priority>().Choice<Support.Priority>())
                .Chain<StringToBool>()
                .Resolve();
    }

    private sealed class ThermostatTrain(IDecider decider) : Train<string, bool>
    {
        public static string? Taken { get; private set; }

        protected override Task<Either<Exception, bool>> Junctions() =>
            AddServices(decider)
                .Scale<string, Temperature>(s =>
                    s.AtLeast(Temperature.Freezing, f => f.Chain(new Note("Freezing")))
                        .AtLeast(Temperature.Mild, m => m.Chain(new Note("Mild")))
                )
                .Chain<StringToBool>()
                .Resolve();

        private sealed class Note(string track) : Junction<string, string>
        {
            public override Task<string> Run(string input)
            {
                Taken = track;
                return Task.FromResult(input);
            }
        }
    }

    private class StringToBool : Junction<string, bool>
    {
        public override Task<bool> Run(string input) => Task.FromResult(true);
    }

    #endregion
}
