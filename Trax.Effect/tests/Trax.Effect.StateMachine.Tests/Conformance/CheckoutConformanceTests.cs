using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Trax.Effect.StateMachine.Tests.Fakes;
using Trax.Effect.StateMachine.Tests.Helpers;

namespace Trax.Effect.StateMachine.Tests.Conformance;

/// <summary>
/// Drives the C# checkout engine over the SAME shared checkout fixtures the TypeScript engine drives.
/// A 3-state, guard-branched, multi-key-context machine — a stronger cross-language proof than the
/// turnstile, and the one that exercises multi-key canonicalization on both runtimes.
/// </summary>
public class CheckoutConformanceTests
{
    public static IEnumerable<TestCaseData> AdvanceFixtures()
    {
        var dir = FixturePaths.AdvanceDir("checkout");
        if (dir is null || !Directory.Exists(dir))
            yield break;
        foreach (
            var path in Directory
                .EnumerateFiles(dir, "*.json")
                .OrderBy(p => p, StringComparer.Ordinal)
        )
            yield return new TestCaseData(path).SetName(
                $"checkout-advance/{Path.GetFileNameWithoutExtension(path)}"
            );
    }

    public static IEnumerable<TestCaseData> RehydrateFixtures()
    {
        var dir = FixturePaths.RehydrateDir("checkout");
        if (dir is null || !Directory.Exists(dir))
            yield break;
        foreach (
            var path in Directory
                .EnumerateFiles(dir, "*.json")
                .OrderBy(p => p, StringComparer.Ordinal)
        )
            yield return new TestCaseData(path).SetName(
                $"checkout-rehydrate/{Path.GetFileNameWithoutExtension(path)}"
            );
    }

    [TestCaseSource(nameof(AdvanceFixtures))]
    public void Advance_matches_the_shared_fixture(string path)
    {
        var fixture = (JsonObject)JsonNode.Parse(File.ReadAllText(path))!;
        var given = ToSnapshot((JsonObject)fixture["given"]!);
        var when = (JsonObject)fixture["when"]!;
        var trigger = when["trigger"]!.GetValue<string>();
        var input = when["input"]?.DeepClone();
        var expect = (JsonObject)fixture["expect"]!;

        var result = TestCheckout.Machine.Advance(given, trigger, input);

        if (expect["outcome"]!.GetValue<string>() == "transitioned")
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
                .Which.Reason.Should()
                .Be(expect["reason"]!.GetValue<string>());
        }
    }

    [TestCaseSource(nameof(RehydrateFixtures))]
    public void Rehydrate_matches_the_shared_fixture(string path)
    {
        var fixture = (JsonObject)JsonNode.Parse(File.ReadAllText(path))!;
        var json =
            fixture["raw"] is JsonNode raw && raw.GetValueKind() == JsonValueKind.String
                ? raw.GetValue<string>()
                : fixture["json"]!.ToJsonString();
        var expect = (JsonObject)fixture["expect"]!;

        var result = TestCheckout.Machine.Rehydrate(json);

        if (expect["result"]!.GetValue<string>() == "ok")
        {
            var expected = ToSnapshot((JsonObject)expect["snapshot"]!);
            result.Should().BeOfType<RehydrationResult.Ok>().Which.Snapshot.Should().Be(expected);
        }
        else
        {
            result
                .Should()
                .BeOfType<RehydrationResult.Error>()
                .Which.Code.Should()
                .Be(expect["code"]!.GetValue<string>());
        }
    }

    [Test]
    public void Serialize_canonicalizes_the_multi_key_wire_handoff_samples()
    {
        if (FixturePaths.MachinesRoot is null)
            Assert.Ignore("Shared checkout fixtures not found (isolated build).");

        var handoff = (JsonObject)
            JsonNode.Parse(
                File.ReadAllText(
                    Path.Combine(FixturePaths.MachinesRoot!, "checkout", "wire-handoff.json")
                )
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
