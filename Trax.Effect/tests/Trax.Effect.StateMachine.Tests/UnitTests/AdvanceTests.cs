using System.Text.Json.Nodes;
using FluentAssertions;
using Trax.Effect.StateMachine.Tests.Fakes;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

public class AdvanceTests
{
    private static Snapshot Locked(JsonObject? ctx = null) =>
        new()
        {
            Machine = "turnstile",
            Version = 1,
            State = "Locked",
            Context = ctx ?? new JsonObject(),
        };

    private static Snapshot Unlocked(string paidWith = "quarter") =>
        new()
        {
            Machine = "turnstile",
            Version = 1,
            State = "Unlocked",
            Context = new JsonObject { ["paidWith"] = paidWith },
        };

    [TestCase("quarter")]
    [TestCase("dollar")]
    public void Advance_Locked_Coin_accepted_transitions_and_records_paidWith(string coin)
    {
        var result = TestTurnstile.Machine.Advance(
            Locked(),
            "Coin",
            new JsonObject { ["coin"] = coin }
        );

        var transitioned = result.Should().BeOfType<AdvanceResult.Transitioned>().Which;
        transitioned.Snapshot.State.Should().Be("Unlocked");
        transitioned.Snapshot.Context["paidWith"]!.GetValue<string>().Should().Be(coin);
    }

    [Test]
    public void Advance_Locked_Coin_penny_is_guard_failed_with_the_guard_message()
    {
        var result = TestTurnstile.Machine.Advance(
            Locked(),
            "Coin",
            new JsonObject { ["coin"] = "penny" }
        );

        var rejected = result.Should().BeOfType<AdvanceResult.Rejected>().Which;
        rejected.Reason.Should().Be(RejectionReasons.GuardFailed);
        rejected.Detail.Should().Be("Only a quarter or a dollar is accepted.");
    }

    [Test]
    public void Advance_Unlocked_Push_clears_the_context()
    {
        var result = TestTurnstile.Machine.Advance(Unlocked(), "Push");

        var transitioned = result.Should().BeOfType<AdvanceResult.Transitioned>().Which;
        transitioned.Snapshot.State.Should().Be("Locked");
        transitioned.Snapshot.Context.Count.Should().Be(0);
    }

    [TestCase("Locked", "Push")]
    [TestCase("Unlocked", "Coin")]
    public void Advance_with_no_wired_edge_is_no_transition(string state, string trigger)
    {
        var snapshot = state == "Locked" ? Locked() : Unlocked();

        var result = TestTurnstile.Machine.Advance(
            snapshot,
            trigger,
            new JsonObject { ["coin"] = "quarter" }
        );

        result
            .Should()
            .BeOfType<AdvanceResult.Rejected>()
            .Which.Reason.Should()
            .Be(RejectionReasons.NoTransition);
    }

    [Test]
    public void Advance_from_an_unknown_state_token_is_no_transition()
    {
        var snapshot = Locked() with { State = "Broken" };

        TestTurnstile
            .Machine.Advance(snapshot, "Coin", new JsonObject { ["coin"] = "quarter" })
            .Should()
            .BeOfType<AdvanceResult.Rejected>()
            .Which.Reason.Should()
            .Be(RejectionReasons.NoTransition);
    }

    [TestCase("Zap")]
    [TestCase("")]
    public void Advance_with_an_unknown_trigger_token_is_no_transition(string trigger)
    {
        TestTurnstile
            .Machine.Advance(Locked(), trigger)
            .Should()
            .BeOfType<AdvanceResult.Rejected>()
            .Which.Reason.Should()
            .Be(RejectionReasons.NoTransition);
    }

    [Test]
    public void Advance_where_a_reducer_produces_an_illegal_context_is_invalid_context()
    {
        var a = new Snapshot
        {
            Machine = "faulty",
            Version = 1,
            State = "A",
            Context = new JsonObject(),
        };

        FaultyMachine
            .Machine.Advance(a, "Bad")
            .Should()
            .BeOfType<AdvanceResult.Rejected>()
            .Which.Reason.Should()
            .Be(RejectionReasons.InvalidContext);
    }

    [Test]
    public void Advance_where_a_reducer_throws_is_internal_error_not_an_exception()
    {
        var a = new Snapshot
        {
            Machine = "faulty",
            Version = 1,
            State = "A",
            Context = new JsonObject(),
        };

        AdvanceResult result = null!;
        var act = () => result = FaultyMachine.Machine.Advance(a, "Boom");

        act.Should().NotThrow();
        result
            .Should()
            .BeOfType<AdvanceResult.Rejected>()
            .Which.Reason.Should()
            .Be(RejectionReasons.InternalError);
    }
}
