using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Trax.Effect.StateMachine.Tests.Fakes;
using Trax.Effect.StateMachine.Tests.Helpers;

namespace Trax.Effect.StateMachine.Tests.Conformance;

/// <summary>
/// Proves the FLUENT authoring surface produces a correct engine: the fluently-built checkout drives the
/// exact same shared fixtures the hand-written and TypeScript engines drive, and the build captures the
/// committed state and the exactly-once effect binding declared inline.
/// </summary>
public class FluentCheckoutTests
{
    [Test]
    public void The_build_captures_the_committed_state_and_the_effect_binding_declared_inline()
    {
        FluentCheckout.Built.Definition.Id.Should().Be("checkout");
        FluentCheckout.Built.Definition.Version.Should().Be(1);
        FluentCheckout.Built.CommittedStates.Should().BeEquivalentTo(new[] { CheckoutState.Paid });

        FluentCheckout.Built.Effects.Should().ContainSingle();
        var effect = FluentCheckout.Built.Effects[0];
        effect.From.Should().Be(CheckoutState.Review);
        effect.Trigger.Should().Be(CheckoutTrigger.Pay);
        effect.To.Should().Be(CheckoutState.Paid);
        effect.EffectType.Should().Be<ICheckoutCharge>();
        effect.KeyPrefix.Should().Be("checkout:charge");
    }

    [Test]
    public void Describe_matches_the_hand_written_and_TypeScript_structure()
    {
        FluentCheckout
            .Machine.Describe()
            .ToJsonString()
            .Should()
            .Be(TestCheckout.Machine.Describe().ToJsonString());
    }

    [TestCaseSource(
        typeof(CheckoutConformanceTests),
        nameof(CheckoutConformanceTests.AdvanceFixtures)
    )]
    public void Advance_matches_the_shared_fixture(string path)
    {
        var fixture = (JsonObject)JsonNode.Parse(File.ReadAllText(path))!;
        var given = ToSnapshot((JsonObject)fixture["given"]!);
        var when = (JsonObject)fixture["when"]!;
        var input = when["input"]?.DeepClone();
        var expect = (JsonObject)fixture["expect"]!;

        var result = FluentCheckout.Machine.Advance(
            given,
            when["trigger"]!.GetValue<string>(),
            input
        );

        if (expect["outcome"]!.GetValue<string>() == "transitioned")
            result
                .Should()
                .BeOfType<AdvanceResult.Transitioned>()
                .Which.Snapshot.Should()
                .Be(ToSnapshot((JsonObject)expect["snapshot"]!));
        else
            result
                .Should()
                .BeOfType<AdvanceResult.Rejected>()
                .Which.Reason.Should()
                .Be(expect["reason"]!.GetValue<string>());
    }

    [TestCaseSource(
        typeof(CheckoutConformanceTests),
        nameof(CheckoutConformanceTests.RehydrateFixtures)
    )]
    public void Rehydrate_matches_the_shared_fixture(string path)
    {
        var fixture = (JsonObject)JsonNode.Parse(File.ReadAllText(path))!;
        var json =
            fixture["raw"] is JsonNode raw && raw.GetValueKind() == JsonValueKind.String
                ? raw.GetValue<string>()
                : fixture["json"]!.ToJsonString();
        var expect = (JsonObject)fixture["expect"]!;

        var result = FluentCheckout.Machine.Rehydrate(json);

        if (expect["result"]!.GetValue<string>() == "ok")
            result
                .Should()
                .BeOfType<RehydrationResult.Ok>()
                .Which.Snapshot.Should()
                .Be(ToSnapshot((JsonObject)expect["snapshot"]!));
        else
            result
                .Should()
                .BeOfType<RehydrationResult.Error>()
                .Which.Code.Should()
                .Be(expect["code"]!.GetValue<string>());
    }

    private static Snapshot ToSnapshot(JsonObject o) =>
        new()
        {
            Machine = o["machine"]!.GetValue<string>(),
            Version = o["version"]!.GetValue<int>(),
            State = o["state"]!.GetValue<string>(),
            Context = (JsonObject)o["context"]!.DeepClone(),
        };
}
