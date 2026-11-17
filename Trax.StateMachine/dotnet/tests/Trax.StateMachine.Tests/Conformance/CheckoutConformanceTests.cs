using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
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
            JsonNode.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(false))!;
        var given = ToSnapshot((JsonObject)fixture["given"]!);
        var when = (JsonObject)fixture["when"]!;
        var trigger = when["trigger"]!.GetValue<string>();
        var input = when["input"]?.DeepClone();
        var expect = (JsonObject)fixture["expect"]!;

        var result = DeclarativeCheckout.Machine.Advance(given, trigger, input);

        if (
            string.Equals(
                expect["outcome"]!.GetValue<string>(),
                "transitioned",
                StringComparison.Ordinal
            )
        )
        {
            var expected = ToSnapshot((JsonObject)expect["snapshot"]!);
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
                .Be(expect["reason"]!.GetValue<string>());
        }
    }

    [TestCaseSource(nameof(RehydrateFixtures))]
    public async Task Rehydrate_matches_the_shared_fixture(string path)
    {
        var fixture = (JsonObject)
            JsonNode.Parse(await File.ReadAllTextAsync(path).ConfigureAwait(false))!;
        var json =
            fixture["raw"] is JsonNode raw && raw.GetValueKind() == JsonValueKind.String
                ? raw.GetValue<string>()
                : fixture["json"]!.ToJsonString();
        var expect = (JsonObject)fixture["expect"]!;

        var result = TestCheckout.Machine.Rehydrate(json);

        if (string.Equals(expect["result"]!.GetValue<string>(), "ok", StringComparison.Ordinal))
        {
            var expected = ToSnapshot((JsonObject)expect["snapshot"]!);
            result.Should().BeOfType<RehydrationResult.Ok>().Which.Snapshot.Should().Be(expected);
        }
        else
        {
            result
                .Should()
                .BeOfType<RehydrationResult.Error>()
                .Which.Code.ToWire()
                .Should()
                .Be(expect["code"]!.GetValue<string>());
        }
    }

    [Test]
    public async Task Serialize_canonicalizes_the_multi_key_wire_handoff_samples()
    {
        var handoff = (JsonObject)
            JsonNode.Parse(
                await File.ReadAllTextAsync(
                        Path.Combine(FixturePaths.MachinesRoot, "checkout", "wire-handoff.json")
                    )
                    .ConfigureAwait(false)
            )!;

        foreach (var sample in (JsonArray)handoff["samples"]!)
        {
            var s = (JsonObject)sample!;
            var built = ToSnapshot((JsonObject)s["build"]!);
            TestCheckout.Machine.Serialize(built).Should().Be(s["snapshot"]!.GetValue<string>());
        }
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
