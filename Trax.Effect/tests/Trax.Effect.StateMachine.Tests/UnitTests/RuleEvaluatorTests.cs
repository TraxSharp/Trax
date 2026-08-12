using System.Text.Json.Nodes;
using FluentAssertions;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

/// <summary>
/// The declarative <see cref="Rule"/> vocabulary is the shared semantics the runtime, the IR, and the
/// generated per-language validators all agree on, so every primitive needs pinned, total behavior: a real
/// predicate for the happy case, and a definite <c>false</c> (never a throw) for a missing or wrong-typed
/// field.
/// </summary>
public class RuleEvaluatorTests
{
    private static readonly JsonObject Empty = new();

    private static bool Eval(Rule rule, JsonObject? context = null, JsonNode? input = null) =>
        RuleEvaluator.Evaluate(rule, context ?? Empty, input);

    #region Present / Absent

    [Test]
    public void Present_is_true_for_a_non_null_field_and_false_when_missing_or_null()
    {
        var ctx = new JsonObject { ["a"] = "x", ["n"] = null };

        Eval(new Rule.Present(RuleSource.Context, "a"), ctx).Should().BeTrue();
        Eval(new Rule.Present(RuleSource.Context, "n"), ctx)
            .Should()
            .BeFalse("a JSON null is not present");
        Eval(new Rule.Present(RuleSource.Context, "missing"), ctx).Should().BeFalse();
    }

    [Test]
    public void Present_reads_the_trigger_input_when_sourced_from_input()
    {
        Eval(
                new Rule.Present(RuleSource.Input, "coin"),
                input: new JsonObject { ["coin"] = "quarter" }
            )
            .Should()
            .BeTrue();
        Eval(new Rule.Present(RuleSource.Input, "coin"), input: null).Should().BeFalse();
    }

    [Test]
    public void Absent_is_the_negation_of_present()
    {
        var ctx = new JsonObject { ["a"] = "x", ["n"] = null };

        Eval(new Rule.Absent(RuleSource.Context, "a"), ctx).Should().BeFalse();
        Eval(new Rule.Absent(RuleSource.Context, "n"), ctx).Should().BeTrue();
        Eval(new Rule.Absent(RuleSource.Context, "missing"), ctx).Should().BeTrue();
    }

    #endregion

    #region OfType

    [Test]
    public void OfType_matches_the_json_kind_and_rejects_others()
    {
        var ctx = new JsonObject
        {
            ["s"] = "x",
            ["n"] = 5,
            ["b"] = true,
            ["arr"] = new JsonArray(1),
            ["obj"] = new JsonObject(),
        };

        Eval(new Rule.OfType(RuleSource.Context, "s", JsonFieldType.String), ctx).Should().BeTrue();
        Eval(new Rule.OfType(RuleSource.Context, "n", JsonFieldType.Number), ctx).Should().BeTrue();
        Eval(new Rule.OfType(RuleSource.Context, "b", JsonFieldType.Boolean), ctx)
            .Should()
            .BeTrue();
        Eval(new Rule.OfType(RuleSource.Context, "arr", JsonFieldType.Array), ctx)
            .Should()
            .BeTrue();
        Eval(new Rule.OfType(RuleSource.Context, "obj", JsonFieldType.Object), ctx)
            .Should()
            .BeTrue();

        Eval(new Rule.OfType(RuleSource.Context, "s", JsonFieldType.Number), ctx)
            .Should()
            .BeFalse();
        Eval(new Rule.OfType(RuleSource.Context, "missing", JsonFieldType.String), ctx)
            .Should()
            .BeFalse();
    }

    #endregion

    #region NonEmpty

    [Test]
    public void NonEmpty_is_true_for_a_non_empty_string_or_array_only()
    {
        var ctx = new JsonObject
        {
            ["s"] = "x",
            ["blank"] = "",
            ["arr"] = new JsonArray(1),
            ["empty"] = new JsonArray(),
            ["num"] = 3,
        };

        Eval(new Rule.NonEmpty(RuleSource.Context, "s"), ctx).Should().BeTrue();
        Eval(new Rule.NonEmpty(RuleSource.Context, "arr"), ctx).Should().BeTrue();
        Eval(new Rule.NonEmpty(RuleSource.Context, "blank"), ctx).Should().BeFalse();
        Eval(new Rule.NonEmpty(RuleSource.Context, "empty"), ctx).Should().BeFalse();
        Eval(new Rule.NonEmpty(RuleSource.Context, "num"), ctx)
            .Should()
            .BeFalse("a number has no emptiness");
        Eval(new Rule.NonEmpty(RuleSource.Context, "missing"), ctx).Should().BeFalse();
    }

    #endregion

    #region OneOf

    [Test]
    public void OneOf_is_true_only_for_a_string_in_the_set()
    {
        string[] coins = ["quarter", "dollar"];

        Eval(
                new Rule.OneOf(RuleSource.Input, "coin", coins),
                input: new JsonObject { ["coin"] = "quarter" }
            )
            .Should()
            .BeTrue();
        Eval(
                new Rule.OneOf(RuleSource.Input, "coin", coins),
                input: new JsonObject { ["coin"] = "penny" }
            )
            .Should()
            .BeFalse();
        Eval(
                new Rule.OneOf(RuleSource.Input, "coin", coins),
                input: new JsonObject { ["coin"] = 25 }
            )
            .Should()
            .BeFalse("a non-string is never a member");
        Eval(new Rule.OneOf(RuleSource.Input, "coin", coins), input: null).Should().BeFalse();
    }

    #endregion

    #region Compare (numeric)

    [TestCase(5, CompareOp.GreaterThan, 0, true)]
    [TestCase(5, CompareOp.GreaterThan, 10, false)]
    [TestCase(5, CompareOp.GreaterOrEqual, 5, true)]
    [TestCase(3, CompareOp.LessThan, 5, true)]
    [TestCase(5, CompareOp.LessOrEqual, 5, true)]
    [TestCase(5, CompareOp.EqualTo, 5, true)]
    [TestCase(5, CompareOp.EqualTo, 6, false)]
    public void Compare_applies_the_operator_to_a_numeric_field(
        int value,
        CompareOp op,
        double target,
        bool expected
    )
    {
        var ctx = new JsonObject { ["total"] = value };
        Eval(new Rule.Compare(RuleSource.Context, "total", op, target), ctx).Should().Be(expected);
    }

    [Test]
    public void Compare_is_false_for_a_missing_or_non_numeric_field()
    {
        var ctx = new JsonObject { ["total"] = "not-a-number" };
        Eval(new Rule.Compare(RuleSource.Context, "total", CompareOp.GreaterThan, 0), ctx)
            .Should()
            .BeFalse();
        Eval(new Rule.Compare(RuleSource.Context, "missing", CompareOp.GreaterThan, 0), ctx)
            .Should()
            .BeFalse();
    }

    [Test]
    public void Compare_is_false_for_an_out_of_range_operator()
    {
        // A corrupt or forward-incompatible op must be a definite reject, not a throw: the class promises the
        // evaluator is total. Guards the `_ => false` arm against being turned into a throw.
        var ctx = new JsonObject { ["total"] = 5 };
        Eval(new Rule.Compare(RuleSource.Context, "total", (CompareOp)999, 5), ctx)
            .Should()
            .BeFalse("an unknown operator is a total reject");
    }

    #endregion

    #region Count (array length)

    [Test]
    public void Count_compares_array_length_and_is_false_for_non_arrays()
    {
        var ctx = new JsonObject { ["items"] = new JsonArray("a", "b"), ["scalar"] = 2 };

        Eval(new Rule.Count(RuleSource.Context, "items", CompareOp.GreaterThan, 0), ctx)
            .Should()
            .BeTrue();
        Eval(new Rule.Count(RuleSource.Context, "items", CompareOp.GreaterOrEqual, 2), ctx)
            .Should()
            .BeTrue();
        Eval(new Rule.Count(RuleSource.Context, "items", CompareOp.GreaterThan, 5), ctx)
            .Should()
            .BeFalse();
        Eval(new Rule.Count(RuleSource.Context, "scalar", CompareOp.GreaterThan, 0), ctx)
            .Should()
            .BeFalse("a scalar has no length");
    }

    #endregion

    #region All / Any

    [Test]
    public void All_requires_every_subrule_and_is_vacuously_true_when_empty()
    {
        var ctx = new JsonObject { ["items"] = new JsonArray("a"), ["total"] = 5 };
        var pass = new Rule.Count(RuleSource.Context, "items", CompareOp.GreaterThan, 0);
        var fail = new Rule.Compare(RuleSource.Context, "total", CompareOp.GreaterThan, 100);

        Eval(new Rule.All([pass, new Rule.Present(RuleSource.Context, "total")]), ctx)
            .Should()
            .BeTrue();
        Eval(new Rule.All([pass, fail]), ctx).Should().BeFalse();
        Eval(new Rule.All([]), ctx).Should().BeTrue();
    }

    [Test]
    public void Any_requires_one_subrule_and_is_false_when_empty()
    {
        var ctx = new JsonObject { ["total"] = 5 };
        var pass = new Rule.Compare(RuleSource.Context, "total", CompareOp.EqualTo, 5);
        var fail = new Rule.Compare(RuleSource.Context, "total", CompareOp.EqualTo, 6);

        Eval(new Rule.Any([fail, pass]), ctx).Should().BeTrue();
        Eval(new Rule.Any([fail]), ctx).Should().BeFalse();
        Eval(new Rule.Any([]), ctx).Should().BeFalse();
    }

    #endregion

    #region Custom

    [Test]
    public void Custom_resolves_through_the_handler_map_and_is_false_when_unregistered()
    {
        var ctx = new JsonObject();
        var handlers = new Dictionary<string, Func<JsonObject, JsonNode?, bool>>
        {
            ["yes"] = (_, _) => true,
            ["no"] = (_, _) => false,
        };

        RuleEvaluator.Evaluate(new Rule.Custom("yes"), ctx, null, handlers).Should().BeTrue();
        RuleEvaluator.Evaluate(new Rule.Custom("no"), ctx, null, handlers).Should().BeFalse();
        RuleEvaluator
            .Evaluate(new Rule.Custom("unregistered"), ctx, null, handlers)
            .Should()
            .BeFalse("an unregistered custom guard is an authoritative reject, not a crash");
        RuleEvaluator
            .Evaluate(new Rule.Custom("yes"), ctx, null, customGuards: null)
            .Should()
            .BeFalse("no handler map at all is also a reject");
    }

    #endregion
}
