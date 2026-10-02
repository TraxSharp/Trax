using System.Text.Json.Serialization;
using FluentAssertions;
using LanguageExt;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Decisions;
using Trax.Core.Junction;
using Trax.Effect.Decisions.SystemOne;
using Trax.Effect.Decisions.SystemOne.Extensions;
using Trax.Effect.Extensions;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Tests.Integration.UnitTests.Decisions;

/// <summary>
/// <see cref="SystemOneDecider"/> vets a train's questions when its chain is read, so what the
/// model refuses on every run (too many options, a question with no words, a state written as a
/// bare number) stops the host from starting instead.
/// </summary>
public class SystemOneVettingTests
{
    private static readonly Uri Server = new("http://localhost:8000/v1/systemone");

    private static SystemOneDecider Decider(Action<SystemOneOptions>? configure = null)
    {
        var options = new SystemOneOptions
        {
            Endpoint = new Uri("https://api.example.test/v1/systemone"),
            Model = "jev-1.13.0",
        };
        configure?.Invoke(options);
        return new SystemOneDecider(options);
    }

    private static DeclaredQuestions Declared(Type state, params Question[] questions) =>
        new("Train", "Decide<State>", state, questions);

    private static readonly Question Fine = new ChoiceQuestion(
        "Lane",
        "Which lane?",
        [new("Left", null), new("Right", null)]
    );

    [Test]
    public void Problems_AQuestionTheModelTakes_HasNone()
    {
        using var decider = Decider();

        decider
            .Problems(
                Declared(
                    typeof(Order),
                    Fine,
                    new ScoreQuestion(
                        "Urgency",
                        "How urgent?",
                        [new("Low", null), new("High", null)]
                    ),
                    new YesNoQuestion("Threat", "Does it threaten anyone?", null, null)
                )
            )
            .Should()
            .BeEmpty();
    }

    [TestCaseSource(nameof(RefusedQuestions))]
    public void Problems_NamesWhatTheModelWouldRefuseOnEveryRun(Question question, string problem)
    {
        using var decider = Decider(o => o.MaxOptions = 3);

        decider
            .Problems(Declared(typeof(Order), question))
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain(problem);
    }

    private static IEnumerable<TestCaseData> RefusedQuestions()
    {
        TestCaseData Case(string name, Question question, string problem) =>
            new TestCaseData(question, problem).SetName($"Problems_Refuses_{name}");

        yield return Case(
            "AQuestionWithNoInstructions",
            new YesNoQuestion("Q", "  ", null, null),
            "'Q' has no instructions"
        );
        yield return Case(
            "AChoiceOfOneOption",
            new ChoiceQuestion("Lane", "Which lane?", [new("Left", null)]),
            "offers 1 option, and the model needs at least 2"
        );
        yield return Case(
            "MoreOptionsThanTheModelTakes",
            new ChoiceQuestion(
                "Lane",
                "Which lane?",
                [new("A", null), new("B", null), new("C", null), new("D", null)]
            ),
            "offers 4 options, and the model accepts at most 3"
        );
        yield return Case(
            "MoreLevelsThanTheModelTakes",
            new ScoreQuestion(
                "Urgency",
                "How urgent?",
                [new("A", null), new("B", null), new("C", null), new("D", null)]
            ),
            "offers 4 levels, and the model accepts at most 3"
        );
        yield return Case(
            "AnOptionNamedTwice",
            new ChoiceQuestion("Lane", "Which lane?", [new("A", null), new("A", null)]),
            "offers the option 'A' more than once"
        );
        yield return Case(
            "AKindOfQuestionTheFormatCannotAsk",
            new OddQuestion("Odd", "What?"),
            "which the System One format cannot ask"
        );
    }

    [Test]
    public void Problems_MoreQuestionsThanOneRequestMayCarry_IsAProblem()
    {
        using var decider = Decider(o => o.MaxQuestions = 1);

        decider
            .Problems(
                Declared(
                    typeof(Order),
                    Fine,
                    new YesNoQuestion("Threat", "Does it threaten anyone?", null, null)
                )
            )
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("asks 2 questions in one request, and the model accepts at most 1");
    }

    [Test]
    public void Problems_AKeyUsedTwice_IsAProblem()
    {
        using var decider = Decider();

        decider
            .Problems(Declared(typeof(Order), Fine, Fine))
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain("asks the question 'Lane' more than once");
    }

    [TestCase(typeof(int), "a number")]
    [TestCase(typeof(decimal), "a number")]
    [TestCase(typeof(int?), "a number")]
    [TestCase(typeof(bool), "true or false")]
    [TestCase(typeof(DayOfWeek), "a number")]
    public void Problems_AStateTypeWrittenAsABareValue_IsAProblem(Type state, string written)
    {
        using var decider = Decider();

        decider
            .Problems(Declared(state, Fine))
            .Should()
            .ContainSingle()
            .Which.Should()
            .Contain($"is written as JSON {written}")
            .And.Contain("a string, an object or an array");
    }

    [TestCase(typeof(string))]
    [TestCase(typeof(Order))]
    [TestCase(typeof(List<Order>))]
    [TestCase(typeof(DateTime))]
    [TestCase(typeof(NamedLane))]
    [TestCase(typeof(object))]
    public void Problems_AStateTypeThatMayBeWrittenAsTheFormatTakes_IsNoProblem(Type state)
    {
        using var decider = Decider();

        decider.Problems(Declared(state, Fine)).Should().BeEmpty();
    }

    [Test]
    public void DeclaredChain_AQuestionNimbleRefuses_IsRefusedWhenTheChainIsRead()
    {
        using var provider = new ServiceCollection()
            .AddTrax(trax =>
                trax.AddEffects(effects => effects.AddNimbleDecider(o => o.Endpoint = Server))
            )
            .AddScopedTraxRoute<IRouteByAisle, RouteByAisle>()
            .BuildServiceProvider();
        using var scope = provider.CreateScope();
        var train = (RouteByAisle)scope.ServiceProvider.GetRequiredService<IRouteByAisle>();

        var chain = train.DeclaredChain();

        chain
            .Refusals.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("the decider 'SystemOneDecider' cannot answer it")
            .And.Contain("offers 27 options, and the model accepts at most 26");
    }

    [Test]
    public void DeclaredChain_AStateNimbleCannotTake_IsRefusedWhenTheChainIsRead()
    {
        using var provider = new ServiceCollection()
            .AddTrax(trax =>
                trax.AddEffects(effects => effects.AddNimbleDecider(o => o.Endpoint = Server))
            )
            .AddScopedTraxRoute<IRouteByCount, RouteByCount>()
            .BuildServiceProvider();
        using var scope = provider.CreateScope();
        var train = (RouteByCount)scope.ServiceProvider.GetRequiredService<IRouteByCount>();

        train
            .DeclaredChain()
            .Refusals.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("its state, a 'Int32', is written as JSON a number");
    }

    public sealed record Order(string Id, decimal Total);

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum NamedLane
    {
        Left,
        Right,
    }

    private sealed record OddQuestion(string Key, string Instructions)
        : Question(Key, Instructions);

    [Asks("Which aisle does this belong in?")]
    public enum Aisle
    {
        A,
        B,
        C,
        D,
        E,
        F,
        G,
        H,
        I,
        J,
        K,
        L,
        M,
        N,
        O,
        P,
        Q,
        R,
        S,
        T,
        U,
        V,
        W,
        X,
        Y,
        Z,
        Overflow,
    }

    [Asks("Is it worth a look?")]
    public sealed class WorthALook;

    public interface IRouteByAisle : IServiceTrain<Order, string>;

    /// <summary>Asks Nimble to choose among 27 aisles, one more than its server accepts.</summary>
    public class RouteByAisle : ServiceTrain<Order, string>, IRouteByAisle
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            Decide<Order>(q => q.Choice<Aisle>()).Chain<Shelve>().Resolve();
    }

    public interface IRouteByCount : IServiceTrain<Order, string>;

    /// <summary>Asks Nimble about a bare number, which the format does not take as a state.</summary>
    public class RouteByCount : ServiceTrain<Order, string>, IRouteByCount
    {
        protected override Task<Either<Exception, string>> Junctions() =>
            Chain<Count>().Decide<int>(q => q.YesNo<WorthALook>()).Chain<Shelve>().Resolve();
    }

    public class Shelve : Junction<Order, string>
    {
        public override Task<string> Run(Order input) => Task.FromResult("shelved");
    }

    public class Count : Junction<Order, int>
    {
        public override Task<int> Run(Order input) => Task.FromResult(1);
    }
}
