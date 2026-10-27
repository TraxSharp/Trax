using System.Diagnostics;
using AwesomeAssertions;
using Trax.Api.Console.Terminal.Machines;

namespace Trax.Api.Console.Terminal.Tests.UnitTests;

/// <summary>
/// What the graph does with a machine it was not drawn for: a big one is laid out in bounded work, a huge one is
/// listed, and a second state of one name or a transition to no state is said rather than thrown or dropped.
/// </summary>
[TestFixture]
public class GraphLayoutTests
{
    [TestCase(false)]
    [TestCase(true)]
    public void A_machine_of_a_hundred_states_and_three_hundred_transitions_is_laid_out_within_a_bounded_search(
        bool vertical
    )
    {
        var states = Enumerable
            .Range(0, 100)
            .Select(i => new GraphState($"S{i}", Initial: i == 0, Final: i == 99))
            .ToList();
        var edges = Enumerable
            .Range(0, 100)
            .SelectMany(i =>
                new[]
                {
                    new GraphEdge($"S{i}", $"S{(i + 1) % 100}", $"next{i}"),
                    new GraphEdge($"S{i}", $"S{(i + 7) % 100}", $"skip{i}"),
                    new GraphEdge($"S{i}", $"S{(i * 13 + 5) % 100}", $"jump{i}"),
                }
            )
            .ToList();

        var clock = Stopwatch.StartNew();
        var lines = GraphLayout.Render(states, edges, "S50", vertical, out var work);
        clock.Stop();
        TestContext.Out.WriteLine(
            $"{clock.ElapsedMilliseconds} ms, {lines.Count} lines, {work.Steps} of {work.Budget} steps, {work.CellsSearched} cells searched"
        );

        // The bound is the work, not the clock: a runner is several times slower than a laptop, and a test of
        // milliseconds fails on the runner without anything having changed. The searches share one budget, and each
        // line past it costs one step to give up. Each search resets the window it covers, which the budget does not
        // count: today these 300 lines cover about 4M cells across and 9M down, and a search that went back to
        // covering the whole drawing for every line would cover several times that.
        work.Steps.Should().BeLessThanOrEqualTo(work.Budget + edges.Count);
        work.CellsSearched.Should().BeLessThan(4 * work.Budget);
        // A ceiling for what neither count sees (layering, ordering, placing), far above what any runner takes.
        clock.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(30));
        var text = string.Join("\n", lines);
        text.Should().Contain("S0").And.Contain("S99");
    }

    [Test]
    public void A_long_chain_of_states_is_listed_without_overflowing_the_stack()
    {
        const int count = 20_000;
        var states = Enumerable
            .Range(0, count)
            .Select(i => new GraphState($"C{i}", Initial: i == 0, Final: i == count - 1))
            .ToList();
        var edges = Enumerable
            .Range(0, count - 1)
            .Select(i => new GraphEdge($"C{i}", $"C{i + 1}", "next"))
            .ToList();

        var lines = GraphLayout.Render(states, edges, current: "C7");

        lines[0].Should().Contain("too many to draw");
        lines.Should().Contain("▶ C7");
        lines.Should().Contain($"  C{count - 1}  (final)");
        lines.Should().Contain("      next ──▶ C1");
    }

    [Test]
    public void A_second_state_of_one_name_is_drawn_once_and_said()
    {
        var lines = GraphLayout.Render(
            [
                new GraphState("Open", Initial: true, Final: false),
                new GraphState("Closed", Initial: false, Final: true),
                new GraphState("Open", Initial: false, Final: false),
            ],
            [new GraphEdge("Open", "Closed", "Close")]
        );
        TestContext.Out.WriteLine(string.Join("\n", lines));

        lines.Should().Contain("Open: declared more than once, drawn once");
        lines.Count(l => l.Contains("│ Open")).Should().Be(1);
    }

    [Test]
    public void A_transition_to_a_state_the_machine_does_not_declare_is_said()
    {
        var lines = GraphLayout.Render(
            [
                new GraphState("Open", Initial: true, Final: false),
                new GraphState("Closed", Initial: false, Final: true),
            ],
            [new GraphEdge("Open", "Closed", "Close"), new GraphEdge("Open", "Ghost", "Haunt")]
        );
        TestContext.Out.WriteLine(string.Join("\n", lines));

        lines.Should().Contain("Open ──▶ Ghost: Haunt  (not a state of this machine)");
    }
}
