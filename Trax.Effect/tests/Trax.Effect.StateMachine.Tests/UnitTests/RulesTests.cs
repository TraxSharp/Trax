using System.Text.Json.Nodes;
using FluentAssertions;
using static Trax.Effect.StateMachine.Rules;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

/// <summary>
/// The ergonomic authoring helpers must build exactly the <see cref="Rule"/> / <see cref="Reduction"/> data
/// the engine and exporter already handle, resolving fields from member expressions to camelCase JSON keys.
/// Every factory and terminal is covered here so the readable surface has no gaps.
/// </summary>
public class RulesTests
{
    private sealed record Ctx
    {
        public string[] Items { get; init; } = [];
        public int Total { get; init; }
        public string Name { get; init; } = "";
        public bool Flag { get; init; }
    }

    private sealed record In
    {
        public string Coin { get; init; } = "";
    }

    [Test]
    public void Input_and_Field_resolve_source_and_camelCase_field()
    {
        Input((In i) => i.Coin).Present().Should().Be(new Rule.Present(RuleSource.Input, "coin"));
        Field((Ctx c) => c.Name)
            .Present()
            .Should()
            .Be(new Rule.Present(RuleSource.Context, "name"));
    }

    [Test]
    public void The_simple_field_matchers_build_their_rules()
    {
        Field((Ctx c) => c.Name).Absent().Should().Be(new Rule.Absent(RuleSource.Context, "name"));
        Field((Ctx c) => c.Name)
            .NonEmpty()
            .Should()
            .Be(new Rule.NonEmpty(RuleSource.Context, "name"));
        Field((Ctx c) => c.Name)
            .OfType(JsonFieldType.String)
            .Should()
            .Be(new Rule.OfType(RuleSource.Context, "name", JsonFieldType.String));
        Field((Ctx c) => c.Total)
            .GreaterThan(0)
            .Should()
            .Be(new Rule.Compare(RuleSource.Context, "total", CompareOp.GreaterThan, 0));
        Field((Ctx c) => c.Total)
            .EqualTo(5)
            .Should()
            .Be(new Rule.Compare(RuleSource.Context, "total", CompareOp.EqualTo, 5));
    }

    [Test]
    public void IsOneOf_carries_the_field_source_and_values()
    {
        var rule = Input((In i) => i.Coin).IsOneOf("quarter", "dollar");

        var oneOf = rule.Should().BeOfType<Rule.OneOf>().Which;
        oneOf.Source.Should().Be(RuleSource.Input);
        oneOf.Field.Should().Be("coin");
        oneOf.Values.Should().Equal("quarter", "dollar");
    }

    [Test]
    public void The_array_count_matchers_build_count_rules()
    {
        Field((Ctx c) => c.Items)
            .CountGreaterThan(0)
            .Should()
            .Be(new Rule.Count(RuleSource.Context, "items", CompareOp.GreaterThan, 0));
        Field((Ctx c) => c.Items)
            .CountAtLeast(2)
            .Should()
            .Be(new Rule.Count(RuleSource.Context, "items", CompareOp.GreaterOrEqual, 2));
    }

    [Test]
    public void The_length_bool_and_array_matchers_build_their_rules()
    {
        Field((Ctx c) => c.Name)
            .LengthGreaterThan(5)
            .Should()
            .Be(new Rule.Length(RuleSource.Context, "name", CompareOp.GreaterThan, 5));
        Field((Ctx c) => c.Name)
            .LengthAtLeast(6)
            .Should()
            .Be(new Rule.Length(RuleSource.Context, "name", CompareOp.GreaterOrEqual, 6));
        Field((Ctx c) => c.Flag)
            .IsTrue()
            .Should()
            .Be(new Rule.BoolEquals(RuleSource.Context, "flag", true));
        Field((Ctx c) => c.Flag)
            .IsFalse()
            .Should()
            .Be(new Rule.BoolEquals(RuleSource.Context, "flag", false));
        Field((Ctx c) => c.Items)
            .ArrayOf(JsonFieldType.String)
            .Should()
            .Be(new Rule.ArrayOf(RuleSource.Context, "items", JsonFieldType.String));
    }

    [Test]
    public void All_and_Any_combine_subrules()
    {
        var a = Field((Ctx c) => c.Name).Present();
        var b = Field((Ctx c) => c.Total).GreaterThan(0);

        All(a, b).Should().BeOfType<Rule.All>().Which.Rules.Should().Equal(a, b);
        Any(a, b).Should().BeOfType<Rule.Any>().Which.Rules.Should().Equal(a, b);
    }

    [Test]
    public void The_no_arg_reducers_build_their_reductions()
    {
        Clear().Should().Be(new Reduction.Clear());
        Reset().Should().Be(new Reduction.Reset());
        Keep().Should().Be(new Reduction.Keep());
    }

    [Test]
    public void Set_from_input_builds_a_single_step_that_copies_an_input_field()
    {
        var reduction = Set((Ctx c) => c.Name).FromInput((In i) => i.Coin);

        var step = reduction
            .Should()
            .BeOfType<Reduction.Set>()
            .Which.Steps.Should()
            .ContainSingle()
            .Which;
        step.Field.Should().Be("name");
        step.Source.Should().BeOfType<ValueSource.FromInput>().Which.Field.Should().Be("coin");
    }

    [Test]
    public void Set_to_value_builds_a_single_step_with_a_constant()
    {
        var reduction = Set((Ctx c) => c.Total).ToValue(0);

        var step = reduction
            .Should()
            .BeOfType<Reduction.Set>()
            .Which.Steps.Should()
            .ContainSingle()
            .Which;
        step.Field.Should().Be("total");
        step.Source.Should()
            .BeOfType<ValueSource.Constant>()
            .Which.Value!.GetValue<int>()
            .Should()
            .Be(0);
    }
}
