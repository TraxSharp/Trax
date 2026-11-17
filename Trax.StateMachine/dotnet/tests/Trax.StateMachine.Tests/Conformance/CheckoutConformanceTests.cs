using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Core.Extensions;
using Trax.StateMachine.Tests.Fakes;
using Trax.StateMachine.Tests.Helpers;

namespace Trax.StateMachine.Tests.Conformance;

/// <summary>
/// Drives the C# checkout engine over the SAME shared checkout fixtures the TypeScript engine drives.
/// A 3-state, guard-branched, multi-key-context machine — a stronger cross-language proof than the
/// turnstile, and the one that exercises multi-key canonicalization on both runtimes. The advance fixtures
/// replay through the declarative checkout, whose <c>Pay</c> declares its input as the twin's IR does.
/// </summary>
public class CheckoutConformanceTests
{
    public static IEnumerable<TestCaseData> AdvanceFixtures()
    {
        var dir = FixturePaths.AdvanceDir("checkout");
        foreach (var path in Directory.EnumerateFiles(dir, "*.json").Order(StringComparer.Ordinal))
            yield return new TestCaseData(path).SetName(
                $"checkout-advance/{Path.GetFileNameWithoutExtension(path)}"
            );
    }

    public static IEnumerable<TestCaseData> RehydrateFixtures()
    {
        var dir = FixturePaths.RehydrateDir("checkout");
        foreach (var path in Directory.EnumerateFiles(dir, "*.json").Order(StringComparer.Ordinal))
            yield return new TestCaseData(path).SetName(
                $"checkout-rehydrate/{Path.GetFileNameWithoutExtension(path)}"
            );
    }

    [TestCaseSource(nameof(AdvanceFixtures))]
    public Task Advance_matches_the_shared_fixture(string path) => Replay(path);

    private static async Task Replay(string path)
    {
        var fixture = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(false)).NotNull();
        var given = ToSnapshot((JsonObject)fixture["given"].NotNull());
        var when = (JsonObject)fixture["when"].NotNull();
        var trigger = when["trigger"].NotNull().GetValue<string>();
        var input = when["input"]?.DeepClone();
        var expect = (JsonObject)fixture["expect"].NotNull();

        var result = DeclarativeCheckout.Machine.Advance(given, trigger, input);

        if (
            string.Equals(
                expect["outcome"].NotNull().GetValue<string>(),
                "transitioned",
                StringComparison.Ordinal
            )
        )
        {
            var expected = ToSnapshot((JsonObject)expect["snapshot"].NotNull());
            result
                .Should()
                .BeOfType<AdvanceResult.Transitioned>()
                .Which.Snapshot.Should()
                .Be(expected);
        }
        else
        {
            result
                .Should()
                .BeOfType<AdvanceResult.Rejected>()
                .Which.Reason.ToWire()
                .Should()
                .Be(expect["reason"].NotNull().GetValue<string>());
        }
    }

    [TestCaseSource(nameof(RehydrateFixtures))]
    public async Task Rehydrate_matches_the_shared_fixture(string path)
    {
        var fixture = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(false)).NotNull();
        var json =
            fixture["raw"] is JsonNode raw && raw.GetValueKind() == JsonValueKind.String
                ? raw.GetValue<string>()
                : fixture["json"].NotNull().ToJsonString();
        var expect = (JsonObject)fixture["expect"].NotNull();

        var result = TestCheckout.Machine.Rehydrate(json);

        if (
            string.Equals(
                expect["result"].NotNull().GetValue<string>(),
                "ok",
                StringComparison.Ordinal
            )
        )
        {
            var expected = ToSnapshot((JsonObject)expect["snapshot"].NotNull());
            result.Should().BeOfType<RehydrationResult.Ok>().Which.Snapshot.Should().Be(expected);
        }
        else
        {
            result
                .Should()
                .BeOfType<RehydrationResult.Error>()
                .Which.Code.ToWire()
                .Should()
                .Be(expect["code"].NotNull().GetValue<string>());
        }
    }

    [Test]
    public async Task Serialize_canonicalizes_the_multi_key_wire_handoff_samples()
    {
        var handoff = (JsonObject)
            JsonNode
                .Parse(
                    await File.ReadAllTextAsync(
                            Path.Combine(FixturePaths.MachinesRoot, "checkout", "wire-handoff.json")
                        )
                        .ConfigureAwait(false)
                )
                .NotNull();

        foreach (var sample in (JsonArray)handoff["samples"].NotNull())
        {
            var s = (JsonObject)sample.NotNull();
            var built = ToSnapshot((JsonObject)s["build"].NotNull());
            TestCheckout
                .Machine.Serialize(built)
                .Should()
                .Be(s["snapshot"].NotNull().GetValue<string>());
        }
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
