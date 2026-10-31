using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using Trax.StateMachine.Persistence;
using Trax.StateMachine.Tests.Helpers;

namespace Trax.StateMachine.Tests.Conformance;

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

    // ts/src/refusalCodes.conformance.test.ts checks the same.
    [Test]
    public void Every_engine_or_client_code_is_held_by_a_kit_case_or_says_why_not()
    {
        var root = FixturePaths.ConformanceRoot;
        var engine = CodesIn(
            new[] { "machines", "strict-input", "plan" }.SelectMany(d =>
                Directory.EnumerateFiles(
                    Path.Combine(root, d),
                    "*.json",
                    SearchOption.AllDirectories
                )
            )
        );
        var client = CodesIn(
            Directory.EnumerateFiles(
                Path.Combine(root, "client", "transcripts"),
                "*.json",
                SearchOption.AllDirectories
            )
        );

        var wrong = new List<string>();
        foreach (
            var entry in JsonNode.Parse(
                File.ReadAllText(Path.Combine(root, "refusal-codes.json"))
            )!["codes"]!.AsArray()
        )
        {
            var code = entry!["code"]!.GetValue<string>();
            var scope = entry["scope"]?.GetValue<string>();
            var uncovered = entry["uncovered"] is not null;
            if (scope is not ("engine" or "client" or "host"))
                wrong.Add($"{code}: scope '{scope}'");
            else if (scope == "host")
            {
                if (uncovered)
                    wrong.Add($"{code}: a host code needs no 'uncovered'");
            }
            else
            {
                var held = (scope == "engine" ? engine : client).Contains(code);
                if (held && uncovered)
                    wrong.Add($"{code}: a case holds it, so 'uncovered' is stale");
                if (!held && !uncovered)
                    wrong.Add($"{code}: no {scope} case holds it");
            }
        }

        wrong
            .Should()
            .BeEmpty("an engine or client code is held by a kit case, or says why it is not");
    }

    // Every string a `reason`, `code` or `error` member holds, at any depth: where a case writes the refusal it
    // expects.
    private static HashSet<string> CodesIn(IEnumerable<string> files)
    {
        var found = new HashSet<string>(StringComparer.Ordinal);
        void Walk(JsonNode? node)
        {
            switch (node)
            {
                case JsonArray array:
                    foreach (var item in array)
                        Walk(item);
                    break;
                case JsonObject obj:
                    foreach (var (key, member) in obj)
                    {
                        if (
                            key is "reason" or "code" or "error"
                            && member is JsonValue value
                            && value.TryGetValue<string>(out var text)
                        )
                            found.Add(text);
                        Walk(member);
                    }
                    break;
            }
        }
        foreach (var file in files)
            Walk(JsonNode.Parse(File.ReadAllText(file)));
        return found;
    }
}
