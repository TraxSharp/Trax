using System.Text.RegularExpressions;
using AwesomeAssertions;
using Trax.Effect.StateMachine.Tests.Helpers;

namespace Trax.Effect.StateMachine.Tests.Conformance;

/// <summary>
/// The IR format is the contract between the IR this engine exports and the TypeScript engine that reads it. A twin
/// records the TypeScript side's <c>irFormatVersion</c> (in <c>Trax.Api.StateMachine/src/rules/irFormat.ts</c>), and
/// an installed engine refuses a format it cannot read, so the two sides must name the same format: a change to
/// what <see cref="IrExporter"/> writes that bumps one bumps both.
/// </summary>
public partial class IrFormatParityTests
{
    [Test]
    public void The_TypeScript_engine_reads_the_IR_format_this_engine_exports()
    {
        var irFormatTs = Path.Combine(
            Directory.GetParent(FixturePaths.MachinesRoot)!.FullName,
            "src",
            "rules",
            "irFormat.ts"
        );
        var match = IrFormatVersion().Match(File.ReadAllText(irFormatTs));
        match.Success.Should().BeTrue("irFormat.ts declares `export const irFormatVersion = <n>;`");

        int.Parse(match.Groups["version"].Value)
            .Should()
            .Be(
                IrExporter.FormatVersion,
                "the TypeScript engine's irFormatVersion and IrExporter.FormatVersion name one IR format; bump "
                    + "both together"
            );
    }

    [GeneratedRegex(@"^export const irFormatVersion = (?<version>\d+);$", RegexOptions.Multiline)]
    private static partial Regex IrFormatVersion();
}
