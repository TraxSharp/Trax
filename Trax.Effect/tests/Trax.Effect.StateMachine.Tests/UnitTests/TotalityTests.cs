using System.Text.Json.Nodes;
using FluentAssertions;
using Trax.Effect.StateMachine.Tests.Fakes;

namespace Trax.Effect.StateMachine.Tests.UnitTests;

/// <summary>
/// Totality is everywhere or nowhere: feed the engine arbitrary garbage and assert it never throws,
/// always returning a typed value. This is the guarantee the API and UI layers depend on.
/// </summary>
public class TotalityTests
{
    private static readonly string[] StateTokens = ["Locked", "Unlocked", "Broken", ""];
    private static readonly string[] TriggerTokens = ["Coin", "Push", "Zap", ""];

    private static readonly JsonNode?[] Inputs =
    [
        null,
        JsonValue.Create(42),
        new JsonObject(),
        new JsonObject { ["coin"] = "quarter" },
        new JsonObject { ["coin"] = 123 },
        new JsonArray(1, 2, 3),
    ];

    [Test]
    public void Advance_never_throws_over_the_full_state_x_trigger_x_input_grid()
    {
        foreach (var state in StateTokens)
        foreach (var trigger in TriggerTokens)
        foreach (var input in Inputs)
        {
            var snapshot = new Snapshot
            {
                Machine = "turnstile",
                Version = 1,
                State = state,
                Context = new JsonObject(),
            };

            AdvanceResult result = null!;
            var act = () =>
                result = TestTurnstile.Machine.Advance(snapshot, trigger, input?.DeepClone());

            act.Should().NotThrow($"Advance({state}, {trigger}) must be total");
            result
                .Should()
                .Match(r => r is AdvanceResult.Transitioned || r is AdvanceResult.Rejected);
        }
    }

    private static readonly string[] GarbageJson =
    [
        "",
        "null",
        "true",
        "42",
        "\"a string\"",
        "[1,2,3]",
        "{",
        "{\"machine\":123}",
        "{\"machine\":\"turnstile\",\"version\":1,\"state\":\"Locked\",\"context\":42}",
        "{\"machine\":\"turnstile\",\"version\":null,\"state\":\"Locked\",\"context\":{}}",
    ];

    [Test]
    public void Rehydrate_never_throws_over_arbitrary_garbage()
    {
        foreach (var json in GarbageJson)
        {
            RehydrationResult result = null!;
            var act = () => result = TestTurnstile.Machine.Rehydrate(json);

            act.Should().NotThrow($"Rehydrate({json}) must be total");
            result.Should().BeOfType<RehydrationResult.Error>();
        }
    }
}
