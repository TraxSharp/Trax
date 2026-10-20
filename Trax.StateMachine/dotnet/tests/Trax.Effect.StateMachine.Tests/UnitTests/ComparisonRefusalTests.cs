using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Effect.StateMachine.Tests.Fakes;
using static Trax.Effect.StateMachine.Rules;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

/// <summary>
/// A comparison that could never hold is refused before it runs: one on a field whose schema type is not a number
/// (an enum, a string, a DateTime are strings on the wire) at <c>Build</c>, and one with a bound that is not finite
/// where the rule is written, since NaN is false against everything and the IR has no number for an infinity.
/// </summary>
public class ComparisonRefusalTests
{
    private enum Tier
    {
        Bronze,
        Gold,
    }

    private sealed record Context
    {
        public Tier Tier { get; init; }

        public string Name { get; init; } = "";

        public DateTime At { get; init; }

        public DateTimeOffset AtOffset { get; init; }

        public int? Score { get; init; }

        public decimal Price { get; init; }
    }

    private sealed record Payload
    {
        public string Code { get; init; } = "";

        public long Count { get; init; }
    }

    // A machine whose Locked -> Coin edge is guarded by the rule, over a typed context and a typed input.
    private static Func<BuiltMachine<TurnstileState, TurnstileTrigger>> Guarded(Rule guard) =>
        () =>
        {
            var m = new MachineBuilder<TurnstileState, TurnstileTrigger>();
            m.Id("compare").Version(1).StartsAt(TurnstileState.Locked, () => new JsonObject());
            m.In(TurnstileState.Locked)
                .Context<Context>()
                .On(TurnstileTrigger.Coin)
                .WithInput<Payload>()
                .When(guard)
                .To(TurnstileState.Unlocked);
            m.In(TurnstileState.Unlocked).Context();
            return m.Build();
        };

    public static IEnumerable<TestCaseData> NonNumberFields()
    {
        yield return new TestCaseData(Field((Context c) => c.Tier).GreaterThan(0), "tier").SetName(
            "an enum"
        );
        yield return new TestCaseData(Field((Context c) => c.Name).AtLeast(1), "name").SetName(
            "a string"
        );
        yield return new TestCaseData(Field((Context c) => c.At).LessThan(1), "at").SetName(
            "a DateTime"
        );
        yield return new TestCaseData(
            Field((Context c) => c.AtOffset).AtMost(1),
            "atOffset"
        ).SetName("a DateTimeOffset");
        yield return new TestCaseData(Input((Payload p) => p.Code).EqualTo(1), "code").SetName(
            "a string on the input"
        );
        yield return new TestCaseData(
            All(Field((Context c) => c.Score).AtLeast(1), Field((Context c) => c.Name).AtLeast(1)),
            "name"
        ).SetName("a string inside All");
    }

    [TestCaseSource(nameof(NonNumberFields))]
    public void A_comparison_on_a_field_that_is_not_a_number_is_refused(Rule guard, string field)
    {
        Guarded(guard)
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage($"*compares the field '{field}'*schema type is string, not a number*");
    }

    [Test]
    public void The_refusal_names_the_edge_and_where_the_field_is()
    {
        Guarded(Input((Payload p) => p.Code).GreaterThan(0))
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "*the guard of Locked -> Coin compares the field 'code' of the input of Coin with GreaterThan*"
            );
    }

    [Test]
    public void A_comparison_on_a_number_field_builds()
    {
        Guarded(
                All(
                    Field((Context c) => c.Score).AtLeast(1),
                    Field((Context c) => c.Price).LessThan(100),
                    Input((Payload p) => p.Count).EqualTo(3)
                )
            )
            .Should()
            .NotThrow("a nullable int, a decimal and a long are numbers on the wire");
    }

    [Test]
    public void A_comparison_in_a_state_requirement_is_checked_too()
    {
        var m = new MachineBuilder<TurnstileState, TurnstileTrigger>();
        m.Id("compare").Version(1).StartsAt(TurnstileState.Locked, () => new JsonObject());
        m.In(TurnstileState.Locked)
            .Context<Context>()
            .Requires(Field((Context c) => c.Tier).AtLeast(1))
            .On(TurnstileTrigger.Coin)
            .To(TurnstileState.Unlocked);
        m.In(TurnstileState.Unlocked).Context();

        var act = () => m.Build();

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "*the Requires of Locked compares the field 'tier' of the context of Locked*"
            );
    }

    [Test]
    public void A_comparison_on_an_untyped_context_is_left_alone()
    {
        // Context() declares no fields, so nothing says what the field holds.
        var m = new MachineBuilder<TurnstileState, TurnstileTrigger>();
        m.Id("compare").Version(1).StartsAt(TurnstileState.Locked, () => new JsonObject());
        m.In(TurnstileState.Locked)
            .Context()
            .On(TurnstileTrigger.Coin)
            .When(Field((Context c) => c.Name).GreaterThan(0))
            .To(TurnstileState.Unlocked);
        m.In(TurnstileState.Unlocked).Context();

        var act = () => m.Build();

        act.Should().NotThrow();
    }

    public static IEnumerable<TestCaseData> NonFiniteBounds()
    {
        var score = Field((Context c) => c.Score);
        yield return new TestCaseData(
            () => score.AtMost(double.PositiveInfinity),
            "AtMost(Infinity)"
        ).SetName("AtMost(+infinity)");
        yield return new TestCaseData(
            () => score.AtLeast(double.NegativeInfinity),
            "AtLeast(-Infinity)"
        ).SetName("AtLeast(-infinity)");
        yield return new TestCaseData(
            () => score.GreaterThan(double.NaN),
            "GreaterThan(NaN)"
        ).SetName("GreaterThan(NaN)");
        yield return new TestCaseData(() => score.LessThan(double.NaN), "LessThan(NaN)").SetName(
            "LessThan(NaN)"
        );
        yield return new TestCaseData(() => score.EqualTo(double.NaN), "EqualTo(NaN)").SetName(
            "EqualTo(NaN)"
        );
    }

    [TestCaseSource(nameof(NonFiniteBounds))]
    public void A_bound_that_is_not_finite_is_refused_where_it_is_written(
        Func<Rule> write,
        string call
    )
    {
        write
            .Should()
            .Throw<ArgumentOutOfRangeException>()
            .WithMessage($"{call} on 'score' needs a finite bound*");
    }

    [Test]
    public void A_hand_written_compare_with_an_infinite_bound_is_refused_at_Build()
    {
        Guarded(
                new Rule.Compare(
                    RuleSource.Context,
                    "score",
                    CompareOp.LessOrEqual,
                    double.PositiveInfinity
                )
            )
            .Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "*compares 'score' with AtMost(Infinity), but a comparison needs a finite bound*"
            );
    }
}
