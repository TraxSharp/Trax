using System.Text.Json.Nodes;
using FluentAssertions;
using Trax.Effect.StateMachine.Tests.Fakes;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

/// <summary>
/// <c>CanFire</c>/<c>AvailableTriggers</c> drive UI enablement, and <c>Describe</c> is the structure
/// oracle the cross-language golden compares against.
/// </summary>
public class IntrospectionTests
{
    private static Snapshot Locked() =>
        new()
        {
            Machine = "turnstile",
            Version = 1,
            State = "Locked",
            Context = new JsonObject(),
        };

    private static Snapshot Unlocked() =>
        new()
        {
            Machine = "turnstile",
            Version = 1,
            State = "Unlocked",
            Context = new JsonObject { ["paidWith"] = "quarter" },
        };

    [Test]
    public void CanFire_reflects_guards_and_wiring()
    {
        TestTurnstile
            .Machine.CanFire(Locked(), "Coin", new JsonObject { ["coin"] = "quarter" })
            .Should()
            .BeTrue();
        TestTurnstile
            .Machine.CanFire(Locked(), "Coin", new JsonObject { ["coin"] = "penny" })
            .Should()
            .BeFalse();
        TestTurnstile.Machine.CanFire(Locked(), "Push").Should().BeFalse();
    }

    [Test]
    public void AvailableTriggers_lists_the_edges_out_of_the_current_state()
    {
        TestTurnstile.Machine.AvailableTriggers(Locked()).Should().Equal("Coin");
        TestTurnstile.Machine.AvailableTriggers(Unlocked()).Should().Equal("Push");
    }

    [Test]
    public void AvailableTriggers_is_empty_for_an_unknown_state()
    {
        TestTurnstile
            .Machine.AvailableTriggers(Locked() with { State = "Broken" })
            .Should()
            .BeEmpty();
    }

    [Test]
    public void Describe_emits_the_sorted_structure()
    {
        var expected =
            "{\"id\":\"turnstile\",\"version\":1,\"initialState\":\"Locked\","
            + "\"states\":[\"Locked\",\"Unlocked\"],"
            + "\"triggers\":[\"Coin\",\"Push\"],"
            + "\"transitions\":[{\"from\":\"Locked\",\"trigger\":\"Coin\",\"to\":\"Unlocked\"},"
            + "{\"from\":\"Unlocked\",\"trigger\":\"Push\",\"to\":\"Locked\"}]}";

        TestTurnstile.Machine.Describe().ToJsonString().Should().Be(expected);
    }
}
