using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Trax.Effect.StateMachine.Tests.Fakes;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

/// <summary>
/// Places where the C# engine accepts or emits something the TypeScript twin does not, so the two runtimes
/// that are documented as behaving identically disagree on the same input.
/// </summary>
public class EngineParityDefectTests
{
    private static readonly SnapshotMachine<TurnstileState, TurnstileTrigger> Turnstile =
        TestTurnstile.Machine;

    // A machine with no validators, so any context rehydrates.
    private static readonly SnapshotMachine<TurnstileState, TurnstileTrigger> Open = new(
        new MachineDefinition<TurnstileState, TurnstileTrigger>
        {
            Id = "open",
            Version = 1,
            InitialState = TurnstileState.Locked,
            CreateInitialContext = () => new JsonObject(),
            Transitions = [],
        }
    );

    private static string Stored(string state, string context) =>
        "{\"machine\":\"turnstile\",\"version\":1,\"state\":\""
        + state
        + "\",\"context\":"
        + context
        + "}";

    [TestCase("1")]
    [TestCase(" Unlocked")]
    [TestCase("Unlocked ")]
    [TestCase("Locked, Unlocked")]
    public void Rehydrate_must_reject_a_state_token_that_is_not_exactly_a_state_name(string token)
    {
        // The TypeScript twin checks `stateSet.has(token)`, so each of these is unknown-state there.
        var result = Turnstile.Rehydrate(Stored(token, "{\"paidWith\":\"quarter\"}"));

        result
            .Should()
            .BeOfType<RehydrationResult.Error>()
            .Which.Code.Should()
            .Be(RehydrationErrorCodes.UnknownState);
    }

    [TestCase("0")]
    [TestCase(" Coin")]
    public void Advance_must_reject_a_trigger_token_that_is_not_exactly_a_trigger_name(
        string trigger
    )
    {
        var locked = Turnstile.Definition.CreateInitialSnapshot();

        var result = Turnstile.Advance(locked, trigger, new JsonObject { ["coin"] = "quarter" });

        result
            .Should()
            .BeOfType<AdvanceResult.Rejected>()
            .Which.Reason.Should()
            .Be(RejectionReasons.NoTransition);
    }

    [Test]
    public void Serialize_must_escape_a_lone_surrogate_like_JSON_stringify()
    {
        // JSON.stringify("\ud800") is the six characters \ud800 (ES2019 well-formed stringify). The C# wire
        // emits the raw lone surrogate, which becomes U+FFFD once the response is UTF-8 encoded.
        var wire = Open.Serialize(
            new Snapshot
            {
                Machine = "m",
                Version = 1,
                State = "S",
                Context = new JsonObject { ["s"] = "\ud800" },
            }
        );

        wire.Should()
            .Be(
                "{\"machine\":\"m\",\"version\":1,\"state\":\"S\",\"context\":{\"s\":\"\\ud800\"}}"
            );
    }

    [Test]
    public void A_context_that_rehydrates_must_serialize_without_throwing()
    {
        // 1e400 is valid JSON and Postgres jsonb stores it, but it overflows a double.
        var result = Open.Rehydrate(
            "{\"machine\":\"open\",\"version\":1,\"state\":\"Locked\",\"context\":{\"n\":1e400}}"
        );

        if (result is RehydrationResult.Ok ok)
        {
            var serialize = () => Open.Serialize(ok.Snapshot);
            serialize
                .Should()
                .NotThrow("a snapshot the engine accepted must have a canonical wire");
        }
    }
}
