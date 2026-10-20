using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Effect.StateMachine.Tests.Fakes;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

/// <summary>
/// What the differential builder's <c>Sample</c> writes into the corpus: a typed input's enum field must
/// carry the spelling the server's stored context carries, which the API's surface writes as the member
/// name.
/// </summary>
public class DifferentialBuilderTests
{
    private enum Priority
    {
        Low,
        HighPriority,
    }

    private sealed record PriorityInput
    {
        public Priority Priority { get; init; }
    }

    private static MachineBuilder<TurnstileState, TurnstileTrigger> Plain()
    {
        var m = new MachineBuilder<TurnstileState, TurnstileTrigger>();
        m.Id("x").Version(1).StartsAt(TurnstileState.Locked, () => new JsonObject());
        m.In(TurnstileState.Locked).Context().On(TurnstileTrigger.Coin).To(TurnstileState.Unlocked);
        m.In(TurnstileState.Unlocked).Context().On(TurnstileTrigger.Push).To(TurnstileState.Locked);
        return m;
    }

    [Test]
    public void Sample_writes_an_enum_by_member_name()
    {
        // A sample input holds an enum; the corpus must spell it the way the server stores it.
        var m = Plain();
        m.Differential(d =>
            d.Sample(TurnstileTrigger.Coin, new PriorityInput { Priority = Priority.HighPriority })
        );

        var built = m.Build();

        var sample = built.Declarative!.Differential!.Samples[TurnstileTrigger.Coin][0]!;
        sample["priority"]!
            .GetValue<string>()
            .Should()
            .Be(
                "HighPriority",
                "because the server stores a machine's input with its surface JSON, which writes an enum "
                    + "by its member name, so the corpus exercises the spelling production stores"
            );
    }
}
