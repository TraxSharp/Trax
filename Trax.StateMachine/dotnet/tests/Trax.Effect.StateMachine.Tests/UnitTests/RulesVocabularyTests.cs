using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Effect.StateMachine.Tests.Fakes;
using Trax.Effect.StateMachine.Tests.Helpers;
using static Trax.Effect.StateMachine.Rules;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

/// <summary>
/// The numeric comparisons beyond <c>GreaterThan</c> and <c>EqualTo</c>: each builds the <see cref="Rule.Compare"/>
/// both runtimes already evaluate, exports its wire op, and is replayed by the differential corpus of the gauge
/// machine, whose guards use all three.
/// </summary>
public class RulesVocabularyTests
{
    private sealed record Card
    {
        public int Rank { get; init; }
    }

    [Test]
    public void LessThan_AtLeast_and_AtMost_build_compare_rules()
    {
        Field((Card c) => c.Rank)
            .LessThan(13)
            .Should()
            .Be(new Rule.Compare(RuleSource.Context, "rank", CompareOp.LessThan, 13));
        Field((Card c) => c.Rank)
            .AtLeast(2)
            .Should()
            .Be(new Rule.Compare(RuleSource.Context, "rank", CompareOp.GreaterOrEqual, 2));
        Field((Card c) => c.Rank)
            .AtMost(10)
            .Should()
            .Be(new Rule.Compare(RuleSource.Context, "rank", CompareOp.LessOrEqual, 10));
    }

    [TestCase(12, true)]
    [TestCase(13, false)]
    public void LessThan_is_strict(int rank, bool holds) =>
        Holds(Field((Card c) => c.Rank).LessThan(13), rank).Should().Be(holds);

    [TestCase(1, false)]
    [TestCase(2, true)]
    public void AtLeast_includes_its_bound(int rank, bool holds) =>
        Holds(Field((Card c) => c.Rank).AtLeast(2), rank).Should().Be(holds);

    [TestCase(10, true)]
    [TestCase(11, false)]
    public void AtMost_includes_its_bound(int rank, bool holds) =>
        Holds(Field((Card c) => c.Rank).AtMost(10), rank).Should().Be(holds);

    [Test]
    public void The_gauge_exports_lt_gte_and_lte()
    {
        var ir = IrExporter.Export(GaugeMachine.Built);

        ir.Should()
            .Contain("\"op\":\"lt\"")
            .And.Contain("\"op\":\"gte\"")
            .And.Contain("\"op\":\"lte\"");
    }

    [Test]
    public void The_gauge_IR_matches_the_committed_golden_the_twin_reads()
    {
        var file = FixturePaths.IrFile("gauge");

        File.ReadAllText(file)
            .TrimEnd('\n')
            .Should()
            .Be(
                IrExporter.Export(GaugeMachine.Built),
                "regenerate it with `trax machine generate` from Trax.Cli's DeclarativeGaugeMachine"
            );
    }

    private static bool Holds(Rule rule, int rank) =>
        RuleEvaluator.Evaluate(rule, new JsonObject { ["rank"] = rank }, null);
}
