using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Trax.StateMachine.Tests.Helpers;

namespace Trax.StateMachine.Tests.Conformance;

/// <summary>
/// The IR format is the contract between the IR this engine exports and the engines that read it. A twin records the
/// TypeScript side's <c>irFormatVersion</c> (in <c>Trax.StateMachine/ts/src/rules/irFormat.ts</c>), and an installed
/// engine refuses a format it cannot read, so every side must name the same format: the TypeScript and Rust engines,
/// <see cref="IrExporter"/>, and the schema in <c>conformance/ir.schema.json</c>. A change to what
/// <see cref="IrExporter"/> writes that bumps one bumps all of them, and the two <c>unreadable-ir</c> cases that sit
/// just outside the range move with it.
/// </summary>
[Category(Categories.CrossEngine)]
public partial class IrFormatParityTests
{
    [Test]
    public void The_TypeScript_engine_reads_the_IR_format_this_engine_exports()
    {
        var irFormatTs = File.ReadAllText(
            Path.Combine(FixturePaths.TypeScriptEngineRoot, "src", "rules", "irFormat.ts")
        );

        Declared(TsIrFormatVersion(), irFormatTs, "irFormat.ts", "irFormatVersion")
            .Should()
            .Be(
                IrExporter.FormatVersion,
                "the TypeScript engine's irFormatVersion and IrExporter.FormatVersion name one IR format; bump "
                    + "both together"
            );
        Declared(TsOldestIrFormatRead(), irFormatTs, "irFormat.ts", "oldestIrFormatRead")
            .Should()
            .Be(
                IrExporter.OldestFormatRead,
                "every engine stops reading a format at the same release"
            );
    }

    [Test]
    public void The_Rust_engine_reads_the_IR_format_this_engine_exports()
    {
        var irRs = File.ReadAllText(Path.Combine(FixturePaths.FolderRoot, "rust", "src", "ir.rs"));

        Declared(RsIrFormatVersion(), irRs, "ir.rs", "IR_FORMAT_VERSION")
            .Should()
            .Be(IrExporter.FormatVersion, "the Rust engine reads the format IrExporter writes");
        Declared(RsOldestIrFormatRead(), irRs, "ir.rs", "OLDEST_IR_FORMAT_READ")
            .Should()
            .Be(
                IrExporter.OldestFormatRead,
                "every engine stops reading a format at the same release"
            );
    }

    [Test]
    public void The_schema_is_of_the_IR_format_this_engine_exports()
    {
        var schema = JsonNode.Parse(
            File.ReadAllText(Path.Combine(FixturePaths.ConformanceRoot, "ir.schema.json"))
        )!;

        schema["$id"]!
            .GetValue<string>()
            .Should()
            .Be($"urn:trax:state-machine:ir-format:{IrExporter.FormatVersion}");
        schema["properties"]!["irFormat"]!["const"]!
            .GetValue<int>()
            .Should()
            .Be(IrExporter.FormatVersion);
    }

    [Test]
    public void The_unreadable_IR_formats_sit_just_outside_the_formats_read()
    {
        var cases = JsonNode.Parse(
            File.ReadAllText(
                Path.Combine(FixturePaths.ConformanceRoot, "unreadable-ir", "cases.json")
            )
        )!["cases"]!
            .AsArray()
            .ToDictionary(
                c => c!["name"]!.GetValue<string>(),
                c => c!["ir"]!["irFormat"]?.GetValue<int>()
            );

        cases["ir-format-too-new"].Should().Be(IrExporter.FormatVersion + 1);
        cases["ir-format-too-old"].Should().Be(IrExporter.OldestFormatRead - 1);
    }

    private static int Declared(Regex declaration, string source, string file, string name)
    {
        var match = declaration.Match(source);
        match.Success.Should().BeTrue($"{file} declares {name}");
        return int.Parse(match.Groups["version"].Value);
    }

    [GeneratedRegex(@"^export const irFormatVersion = (?<version>\d+);$", RegexOptions.Multiline)]
    private static partial Regex TsIrFormatVersion();

    [GeneratedRegex(
        @"^export const oldestIrFormatRead = (?<version>\d+);$",
        RegexOptions.Multiline
    )]
    private static partial Regex TsOldestIrFormatRead();

    [GeneratedRegex(
        @"^pub const IR_FORMAT_VERSION: i64 = (?<version>\d+);$",
        RegexOptions.Multiline
    )]
    private static partial Regex RsIrFormatVersion();

    [GeneratedRegex(
        @"^pub const OLDEST_IR_FORMAT_READ: i64 = (?<version>\d+);$",
        RegexOptions.Multiline
    )]
    private static partial Regex RsOldestIrFormatRead();
}
