using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.Effect.StateMachine.Persistence;
using Trax.Effect.StateMachine.Tests.Helpers;

namespace Trax.Effect.StateMachine.Tests.Conformance;

/// <summary>
/// A refusal code is one closed set, <see cref="RefusalCode"/>, that both engines, the GraphQL schema and the
/// TypeScript package hold. A code that is a free string somewhere reaches no client type, so every public member
/// that carries one is typed by the enum, and the enum equals <c>conformance/refusal-codes.json</c> member for
/// member, which the TypeScript package and every other engine check themselves against too.
/// </summary>
public class RefusalCodeParityTests
{
    [Test]
    public void Every_public_code_or_reason_is_a_RefusalCode()
    {
        var assemblies = new[]
        {
            typeof(SnapshotMachine<,>).Assembly,
            typeof(SnapshotProblem).Assembly,
        };

        var free = assemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsVisible)
            .SelectMany(t =>
                t.GetProperties(
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly
                )
            )
            .Where(p => p.Name is "Code" or "Reason")
            .Where(p =>
                (Nullable.GetUnderlyingType(p.PropertyType) ?? p.PropertyType)
                != typeof(RefusalCode)
            )
            .Select(p => $"{p.DeclaringType!.FullName}.{p.Name}: {p.PropertyType.Name}")
            .ToList();

        free.Should()
            .BeEmpty("a refusal code is a RefusalCode, so a client can switch on every one");
    }

    [Test]
    public void The_wire_spelling_is_upper_snake_case_and_reads_back()
    {
        RefusalCode.GuardFailed.ToWire().Should().Be("GUARD_FAILED");
        RefusalCode.ClientOutdated.ToWire().Should().Be("CLIENT_OUTDATED");

        foreach (var code in RefusalCodes.All)
        {
            code.ToWire().Should().MatchRegex("^[A-Z]+(_[A-Z]+)*$");
            RefusalCodes.TryParse(code.ToWire(), out var back).Should().BeTrue();
            back.Should().Be(code);
            JsonSerializer.Serialize(code).Should().Be($"\"{code.ToWire()}\"");
        }
    }

    [Test]
    public void A_code_an_older_node_recorded_in_kebab_case_still_reads()
    {
        RefusalCodes.TryParse("client-outdated", out var code).Should().BeTrue();
        code.Should().Be(RefusalCode.ClientOutdated);
        JsonSerializer
            .Deserialize<RefusalCode>("\"guard-failed\"")
            .Should()
            .Be(RefusalCode.GuardFailed);
        RefusalCodes.TryParse("Guard-Failed", out _).Should().BeFalse();
        RefusalCodes.TryParse("guard_failed", out _).Should().BeFalse();
    }

    [Test]
    public void The_shared_registry_lists_the_same_codes_in_the_same_order()
    {
        var registry = JsonNode.Parse(
            File.ReadAllText(Path.Combine(FixturePaths.ConformanceRoot, "refusal-codes.json"))
        )!["codes"]!
            .AsArray()
            .Select(c =>
                (Name: c!["name"]!.GetValue<string>(), Wire: c["code"]!.GetValue<string>())
            )
            .ToList();

        registry
            .Should()
            .Equal(
                RefusalCodes.All.Select(c => (Name: c.ToString(), Wire: c.ToWire())),
                "conformance/refusal-codes.json is the one set every engine and client checks itself against"
            );
    }
}
