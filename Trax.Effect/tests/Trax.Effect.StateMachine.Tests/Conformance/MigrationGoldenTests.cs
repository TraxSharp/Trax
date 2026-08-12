using System.Text.Json.Nodes;
using FluentAssertions;
using Trax.Effect.StateMachine.Tests.Fakes;
using Trax.Effect.StateMachine.Tests.Helpers;

namespace Trax.Effect.StateMachine.Tests.Conformance;

/// <summary>
/// Replays the shared migration golden through the real forward-migration path. Where the differential
/// guards machine LOGIC, this guards migration CORRECTNESS against real stored shapes: each case is a stored
/// older-version snapshot and the exact canonical wire it must become, so a migration that drops, renames, or
/// reorders a surviving field fails here, and both engines replaying the same file cannot diverge. The v2
/// turnstile (<see cref="TestTurnstileV2"/>) is the machine under test.
/// </summary>
public class MigrationGoldenTests
{
    public static IEnumerable<TestCaseData> MigrationCases()
    {
        var file = FixturePaths.MigrationFile("turnstile");
        if (file is null || !File.Exists(file))
            yield break;

        var golden = (JsonObject)JsonNode.Parse(File.ReadAllText(file))!;
        foreach (var node in (JsonArray)golden["cases"]!)
        {
            var @case = (JsonObject)node!;
            var name = @case["name"]!.GetValue<string>();
            yield return new TestCaseData(
                @case["stored"]!.GetValue<string>(),
                @case["expected"]!.GetValue<string>()
            ).SetName($"migration/{name}");
        }
    }

    [Test]
    public void Shared_migration_golden_is_reachable()
    {
        if (FixturePaths.MachinesRoot is null)
            Assert.Ignore(
                "Shared Trax.Api.StateMachine/machines fixtures not found (isolated build). In CI they are supplied by the Trax.StateMachine.Fixtures package."
            );

        File.Exists(FixturePaths.MigrationFile("turnstile")).Should().BeTrue();
    }

    [TestCaseSource(nameof(MigrationCases))]
    public void Stored_snapshot_migrates_to_the_exact_expected_wire(string stored, string expected)
    {
        var result = TestTurnstileV2.Machine.Rehydrate(stored);

        var ok = result.Should().BeOfType<RehydrationResult.Ok>().Which;
        // The migrated snapshot must re-serialize to the exact committed wire, byte-for-byte. This is what
        // makes a dropped/renamed field fail: the surviving fields are pinned in `expected`.
        TestTurnstileV2.Machine.Serialize(ok.Snapshot).Should().Be(expected);
    }
}
