using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Effect.StateMachine.Tests.Fakes;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

/// <summary>
/// Which refusal wins when an advance breaks several rules at once: no edge from the state comes before a bad
/// input, and a bad input comes before the guard, which never sees it.
/// </summary>
[Property("adr", TriggerInputValidationTests.Adr)]
public class RefusalPrecedenceTests
{
    private const string Adr = TriggerInputValidationTests.Adr;

    private static JsonObject NumberAsString()
    {
        var input = RangeMachine.ValidInput();
        input["from"] = "2020";
        return input;
    }

    // Green today: Ranged has no Build edge, and the engine looks for one before anything else.
    [Test]
    public void Invalid_input_on_a_trigger_with_no_edge_from_this_state_is_no_transition()
    {
        var result = RangeMachine.Machine.Advance(
            RangeMachine.Ranged(),
            nameof(RangeTrigger.Build),
            NumberAsString()
        );

        result
            .Should()
            .BeOfType<AdvanceResult.Rejected>()
            .Which.Reason.Should()
            .Be(
                RejectionReasons.NoTransition,
                $"a trigger with no edge from this state is refused before its input is read ({Adr})"
            );
    }

    [Test]
    public void Invalid_input_is_reported_before_the_guard_runs()
    {
        // The guard (from > -1) fails on the string too, so today the answer is guard-failed.
        var result = RangeMachine.Machine.Advance(
            RangeMachine.Open(),
            nameof(RangeTrigger.Build),
            NumberAsString()
        );

        result
            .Should()
            .BeOfType<AdvanceResult.Rejected>()
            .Which.Reason.Should()
            .Be(
                RejectionReasons.InvalidInput,
                $"the input is checked against its schema before any guard reads it ({Adr})"
            );
    }

    [Test]
    public void A_guard_never_sees_an_input_that_failed_its_schema()
    {
        var calls = 0;
        var m = new MachineBuilder<RangeState, RangeTrigger>();
        m.Id("range").StartsAt(RangeState.Open, () => new JsonObject());
        m.In(RangeState.Open)
            .Context()
            .On(RangeTrigger.Build)
            .WithInput<RangeMachine.RangeInput>()
            .When(
                (_, _) =>
                {
                    calls++;
                    return true;
                }
            )
            .To(RangeState.Ranged);
        m.In(RangeState.Ranged).Context();
        var engine = m.Build().Engine;

        engine.Advance(
            new Snapshot
            {
                Machine = "range",
                Version = 1,
                State = nameof(RangeState.Open),
                Context = new JsonObject(),
            },
            nameof(RangeTrigger.Build),
            NumberAsString()
        );

        calls.Should().Be(0, $"a guard only ever reads an input that matched its schema ({Adr})");
    }
}
