using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using Trax.Effect.StateMachine.Tests.Fakes;
using Trax.Effect.StateMachine.Tests.Helpers;

namespace Trax.Effect.StateMachine.Tests.Conformance;

/// <summary>
/// Drives the C# turnstile engine over the SAME shared fixtures the TypeScript engine drives. If both
/// suites stay green over one set of files, the two engines cannot disagree — that is the cross-language
/// guarantee (PD1), proven by data rather than by generating one engine from the other.
/// </summary>
public class ConformanceTests
{
    public static IEnumerable<TestCaseData> AdvanceFixtures()
    {
        var dir = FixturePaths.AdvanceDir("turnstile");
        if (dir is null || !Directory.Exists(dir))
            yield break;
        foreach (
            var path in Directory
                .EnumerateFiles(dir, "*.json")
                .OrderBy(p => p, StringComparer.Ordinal)
        )
            yield return new TestCaseData(path).SetName(
                $"advance/{Path.GetFileNameWithoutExtension(path)}"
            );
    }

    public static IEnumerable<TestCaseData> RehydrateFixtures()
    {
        var dir = FixturePaths.RehydrateDir("turnstile");
        if (dir is null || !Directory.Exists(dir))
            yield break;
        foreach (
            var path in Directory
                .EnumerateFiles(dir, "*.json")
                .OrderBy(p => p, StringComparer.Ordinal)
        )
            yield return new TestCaseData(path).SetName(
                $"rehydrate/{Path.GetFileNameWithoutExtension(path)}"
            );
    }

    [Test]
    public void Shared_fixtures_are_reachable()
    {
        if (FixturePaths.MachinesRoot is null)
            Assert.Ignore(
                "Shared Trax.Api.StateMachine/machines fixtures not found (isolated build). In CI they are supplied by the Trax.StateMachine.Fixtures package."
            );

        Directory.Exists(FixturePaths.AdvanceDir("turnstile")).Should().BeTrue();
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

        var result = TestTurnstile.Machine.Advance(given, trigger, input);

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

        var result = TestTurnstile.Machine.Rehydrate(json);

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

    private static Snapshot ToSnapshot(JsonObject o) =>
        new()
        {
            Machine = o["machine"]!.GetValue<string>(),
            Version = o["version"]!.GetValue<int>(),
            State = o["state"]!.GetValue<string>(),
            Context = (JsonObject)o["context"]!.DeepClone(),
        };
}
