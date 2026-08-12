using System.Text.Json.Nodes;
using FluentAssertions;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

/// <summary>
/// The declarative reducer vocabulary produces a transition's destination context. Each shape is pinned:
/// carry-forward, clear, reset-to-initial, and clone-and-set (the common cases from the machine catalog). It
/// must be non-mutating (the source context is never aliased) so an author cannot accidentally corrupt the
/// stored draft.
/// </summary>
public class ReductionEvaluatorTests
{
    private static readonly JsonObject NoInitial = new();

    private static JsonObject Apply(
        Reduction reduction,
        JsonObject context,
        JsonNode? input = null
    ) => ReductionEvaluator.Apply(reduction, context, input, NoInitial);

    [Test]
    public void Keep_carries_the_context_forward_as_an_independent_copy()
    {
        var context = new JsonObject { ["a"] = 1 };

        var result = Apply(new Reduction.Keep(), context);

        result.ToJsonString().Should().Be(context.ToJsonString());
        result["a"] = 99; // mutating the result must not touch the source
        context["a"]!.GetValue<int>().Should().Be(1);
    }

    [Test]
    public void Clear_produces_an_empty_context()
    {
        Apply(new Reduction.Clear(), new JsonObject { ["a"] = 1 }).ToJsonString().Should().Be("{}");
    }

    [Test]
    public void Reset_produces_a_clone_of_the_initial_context()
    {
        var initial = new JsonObject { ["items"] = new JsonArray(), ["total"] = 0 };

        var result = ReductionEvaluator.Apply(
            new Reduction.Reset(),
            new JsonObject { ["stale"] = 1 },
            null,
            initial
        );

        result.ToJsonString().Should().Be(initial.ToJsonString());
        result["total"] = 5; // independent of the source initial context
        initial["total"]!.GetValue<int>().Should().Be(0);
    }

    [Test]
    public void Set_clones_the_context_and_copies_a_field_from_input()
    {
        var context = new JsonObject { ["items"] = new JsonArray("book"), ["receipt"] = null };
        var input = new JsonObject { ["receipt"] = "rcpt_1" };

        var result = Apply(
            new Reduction.Set([new SetStep("receipt", new ValueSource.FromInput("receipt"))]),
            context,
            input
        );

        // items survived (clone-and-set, not replace), receipt filled from input.
        result["items"]!
            .AsArray()
            .Should()
            .ContainSingle()
            .Which.GetValue<string>()
            .Should()
            .Be("book");
        result["receipt"]!.GetValue<string>().Should().Be("rcpt_1");
    }

    [Test]
    public void Set_supports_constants_and_multiple_steps_and_a_missing_input_becomes_null()
    {
        var result = Apply(
            new Reduction.Set([
                new SetStep("paidWith", new ValueSource.FromInput("coin")),
                new SetStep("flag", new ValueSource.Constant(true)),
                new SetStep("missing", new ValueSource.FromInput("absent")),
            ]),
            new JsonObject(),
            new JsonObject { ["coin"] = "quarter" }
        );

        result["paidWith"]!.GetValue<string>().Should().Be("quarter");
        result["flag"]!.GetValue<bool>().Should().BeTrue();
        result["missing"].Should().BeNull("a missing input field resolves to JSON null");
    }

    [Test]
    public void Custom_uses_the_registered_reducer_and_carries_forward_when_unregistered()
    {
        var context = new JsonObject { ["a"] = 1 };
        var reducers = new Dictionary<string, Func<JsonObject, JsonNode?, JsonObject>>
        {
            ["double"] = (ctx, _) => new JsonObject { ["a"] = ctx["a"]!.GetValue<int>() * 2 },
        };

        ReductionEvaluator.Apply(
            new Reduction.Custom("double"),
            context,
            null,
            NoInitial,
            reducers
        )["a"]!
            .GetValue<int>()
            .Should()
            .Be(2);

        ReductionEvaluator
            .Apply(new Reduction.Custom("unregistered"), context, null, NoInitial, reducers)
            .ToJsonString()
            .Should()
            .Be(
                context.ToJsonString(),
                "an unregistered reducer carries the context forward, never throws"
            );
    }
}
