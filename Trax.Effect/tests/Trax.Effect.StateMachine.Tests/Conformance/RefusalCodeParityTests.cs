using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Trax.Effect.StateMachine.Persistence;
using Trax.Effect.StateMachine.Tests.Helpers;

namespace Trax.Effect.StateMachine.Tests.Conformance;

/// <summary>
/// A refusal code is one closed set, <see cref="RefusalCode"/>, that both engines, the GraphQL schema and the
/// TypeScript package hold. A code that is a free string somewhere reaches no client type, so every public member
/// that carries one is typed by the enum, and the TypeScript package's list equals it member for member.
/// </summary>
public partial class RefusalCodeParityTests
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
    public void The_TypeScript_package_exports_the_same_codes()
    {
        if (FixturePaths.MachinesRoot is null)
            Assert.Ignore(
                "Trax.Api.StateMachine not found (isolated build), so there is no TypeScript package to compare."
            );

        var resultsTs = Path.Combine(
            Directory.GetParent(FixturePaths.MachinesRoot)!.FullName,
            "src",
            "results.ts"
        );
        var block = RefusalCodeBlock().Match(File.ReadAllText(resultsTs));
        block
            .Success.Should()
            .BeTrue("results.ts declares `export const RefusalCode = { ... } as const;`");

        var ts = TsMember()
            .Matches(block.Groups["body"].Value)
            .Select(m => (Name: m.Groups["name"].Value, Wire: m.Groups["wire"].Value))
            .ToList();

        ts.Should()
            .Equal(
                RefusalCodes.All.Select(c => (Name: c.ToString(), Wire: c.ToWire())),
                "the TypeScript RefusalCode and the C# enum are one set, in one order"
            );
    }

    [GeneratedRegex(
        @"export const RefusalCode = \{(?<body>.*?)\} as const;",
        RegexOptions.Singleline
    )]
    private static partial Regex RefusalCodeBlock();

    [GeneratedRegex(@"(?<name>[A-Z][A-Za-z]*): ['""](?<wire>[A-Z_]+)['""]")]
    private static partial Regex TsMember();
}
