using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Core.Extensions;
using Trax.StateMachine.Tests.Fakes;
using Trax.StateMachine.Tests.Helpers;

namespace Trax.StateMachine.Tests.Conformance;

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

    // The shared fixtures but those that send a bad input: this checkout's guards and reducers are delegates, which
    // read the input themselves, so it declares no input schema and no invalid-input can come from it. The
    // declarative checkout replays those (CheckoutConformanceTests).
    public static async Task<IEnumerable<TestCaseData>> DelegateAdvanceFixtures()
    {
        var fixtures = new List<TestCaseData>();
        foreach (var c in CheckoutConformanceTests.AdvanceFixtures())
        {
            var text = await File.ReadAllTextAsync((string)c.Arguments[0].NotNull())
                .ConfigureAwait(false);
            if (
                !string.Equals(
                    JsonNode
                        .Parse(text)
                        .NotNull()["expect"]
                        .NotNull()["reason"]
                        ?.GetValue<string>(),
                    RefusalCode.InvalidInput.ToWire(),
                    StringComparison.Ordinal
                )
            )
                fixtures.Add(c);
        }
        return fixtures;
    }

    [TestCaseSource(nameof(DelegateAdvanceFixtures))]
    public async Task Advance_matches_the_shared_fixture(string path)
    {
        var fixture = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(false)).NotNull();
        var given = ToSnapshot((JsonObject)fixture["given"].NotNull());
        var when = (JsonObject)fixture["when"].NotNull();
        var input = when["input"]?.DeepClone();
        var expect = (JsonObject)fixture["expect"].NotNull();

        var result = FluentCheckout.Machine.Advance(
            given,
            when["trigger"].NotNull().GetValue<string>(),
            input
        );

        if (
            string.Equals(
                expect["outcome"].NotNull().GetValue<string>(),
                "transitioned",
                StringComparison.Ordinal
            )
        )
            result
                .Should()
                .BeOfType<AdvanceResult.Transitioned>()
                .Which.Snapshot.Should()
                .Be(ToSnapshot((JsonObject)expect["snapshot"].NotNull()));
        else
            result
                .Should()
                .BeOfType<AdvanceResult.Rejected>()
                .Which.Reason.ToWire()
                .Should()
                .Be(expect["reason"].NotNull().GetValue<string>());
    }

    [TestCaseSource(
        typeof(CheckoutConformanceTests),
        nameof(CheckoutConformanceTests.RehydrateFixtures)
    )]
    public async Task Rehydrate_matches_the_shared_fixture(string path)
    {
        var fixture = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(false)).NotNull();
        var json =
            fixture["raw"] is JsonNode raw && raw.GetValueKind() == JsonValueKind.String
                ? raw.GetValue<string>()
                : fixture["json"].NotNull().ToJsonString();
        var expect = (JsonObject)fixture["expect"].NotNull();

        var result = FluentCheckout.Machine.Rehydrate(json);

        if (
            string.Equals(
                expect["result"].NotNull().GetValue<string>(),
                "ok",
                StringComparison.Ordinal
            )
        )
            result
                .Should()
                .BeOfType<RehydrationResult.Ok>()
                .Which.Snapshot.Should()
                .Be(ToSnapshot((JsonObject)expect["snapshot"].NotNull()));
        else
            result
                .Should()
                .BeOfType<RehydrationResult.Error>()
                .Which.Code.ToWire()
                .Should()
                .Be(expect["code"].NotNull().GetValue<string>());
    }

    private static Snapshot ToSnapshot(JsonObject o) =>
        new()
        {
            Machine = o["machine"].NotNull().GetValue<string>(),
            Version = o["version"].NotNull().GetValue<int>(),
            State = o["state"].NotNull().GetValue<string>(),
            Context = (JsonObject)o["context"].NotNull().DeepClone(),
        };
}
