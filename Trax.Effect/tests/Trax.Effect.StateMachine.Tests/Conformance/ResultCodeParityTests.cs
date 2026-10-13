using System.Reflection;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Trax.Effect.StateMachine.Tests.Helpers;

namespace Trax.Effect.StateMachine.Tests.Conformance;

/// <summary>
/// The rejection reasons are part of the cross-language contract: a client reads the code the server answers with,
/// so the TypeScript twin's <c>RejectionReasons</c> (in <c>Trax.Api.StateMachine/src/results.ts</c>) lists exactly the
/// codes the C# <see cref="RejectionReasons"/> declares, with the same values.
/// </summary>
public partial class ResultCodeParityTests
{
    [Test]
    public void The_TypeScript_RejectionReasons_match_the_engines()
    {
        if (FixturePaths.MachinesRoot is null)
            Assert.Ignore(
                "Trax.Api.StateMachine not found (isolated build), so there is no TypeScript twin to compare."
            );

        var resultsTs = Path.Combine(
            Directory.GetParent(FixturePaths.MachinesRoot)!.FullName,
            "src",
            "results.ts"
        );
        var source = File.ReadAllText(resultsTs);
        var block = RejectionReasonsBlock().Match(source);
        block
            .Success.Should()
            .BeTrue("results.ts declares `export const RejectionReasons = { ... }`");

        var typeScript = Member()
            .Matches(block.Groups["body"].Value)
            .ToDictionary(m => m.Groups["name"].Value, m => m.Groups["value"].Value);

        var csharp = typeof(RejectionReasons)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .ToDictionary(f => f.Name, f => (string)f.GetRawConstantValue()!);

        typeScript
            .Should()
            .BeEquivalentTo(
                csharp,
                "the TypeScript RejectionReasons is the same contract as the C# one, name for name and code for code"
            );
    }

    [GeneratedRegex(
        @"export const RejectionReasons = \{(?<body>.*?)\} as const;",
        RegexOptions.Singleline
    )]
    private static partial Regex RejectionReasonsBlock();

    [GeneratedRegex(@"^\s*(?<name>[A-Za-z]+):\s*'(?<value>[^']+)'", RegexOptions.Multiline)]
    private static partial Regex Member();
}
