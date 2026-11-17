using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Tests.Fakes;
using Trax.StateMachine.Tests.Helpers;

namespace Trax.StateMachine.Tests.Conformance;

/// <summary>
/// Drives the C# turnstile engine over the SAME shared fixtures the TypeScript engine drives. If both
/// suites stay green over one set of files, the two engines cannot disagree — that is the cross-language
/// guarantee (PD1), proven by data rather than by generating one engine from the other. The advance fixtures
/// replay through the declarative turnstile, whose <c>Coin</c> declares its input, as the twin's IR does: the
/// fixtures with a bad input expect <c>INVALID_INPUT</c>, which only a declared input schema can give.
/// </summary>
public class ConformanceTests
{
    public static IEnumerable<TestCaseData> AdvanceFixtures()
    {
        var dir = FixturePaths.AdvanceDir("turnstile");
        foreach (var path in Directory.EnumerateFiles(dir, "*.json").Order(StringComparer.Ordinal))
            yield return new TestCaseData(path).SetName(
                $"advance/{Path.GetFileNameWithoutExtension(path)}"
            );
    }

    public static IEnumerable<TestCaseData> RehydrateFixtures()
    {
        var dir = FixturePaths.RehydrateDir("turnstile");
        foreach (var path in Directory.EnumerateFiles(dir, "*.json").Order(StringComparer.Ordinal))
            yield return new TestCaseData(path).SetName(
                $"rehydrate/{Path.GetFileNameWithoutExtension(path)}"
            );
    }

    [Test]
    public void Shared_fixtures_are_reachable()
    {
        Directory.Exists(FixturePaths.AdvanceDir("turnstile")).Should().BeTrue();
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

        var result = DeclarativeTurnstile.Machine.Advance(given, trigger, input);

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

        var result = TestTurnstile.Machine.Rehydrate(json);

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

    private static Snapshot ToSnapshot(JsonObject o) =>
        new()
        {
            Machine = o["machine"]!.GetValue<string>(),
            Version = o["version"]!.GetValue<int>(),
            State = o["state"]!.GetValue<string>(),
            Context = (JsonObject)o["context"]!.DeepClone(),
        };
}
