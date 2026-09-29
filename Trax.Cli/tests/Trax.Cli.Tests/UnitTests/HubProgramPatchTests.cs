using FluentAssertions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Trax.Cli.Generator;

namespace Trax.Cli.Tests.UnitTests;

/// <summary>
/// <see cref="TraxProjectGenerator.PatchProgramCs"/> against the hub template's real <c>Program.cs</c>
/// (<c>Fixtures/HubTemplate/Program.cs.txt</c>, copied from Trax.Samples' <c>templates/content/Trax.Samples.Hub</c>),
/// not a toy one. The toy file had its last <c>using</c> at the top; the template's is a
/// <c>using var</c> inside a <c>using (...)</c> block, which is where the patch used to put a using directive.
/// </summary>
[TestFixture]
public class HubProgramPatchTests
{
    private string _hubDir = null!;

    [SetUp]
    public void SetUp()
    {
        _hubDir = Path.Combine(Path.GetTempPath(), $"trax-cli-hub-patch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_hubDir);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_hubDir))
            Directory.Delete(_hubDir, recursive: true);
    }

    [Test]
    public void PatchProgramCs_OnTheHubTemplatesProgram_StillParses()
    {
        File.Copy(TemplateProgram, Path.Combine(_hubDir, "Program.cs"));

        TraxProjectGenerator.PatchProgramCs(_hubDir, "Demo");

        var patched = File.ReadAllText(Path.Combine(_hubDir, "Program.cs"));
        var errors = CSharpSyntaxTree
            .ParseText(patched)
            .GetDiagnostics()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .Select(d => d.ToString());
        errors.Should().BeEmpty("the patched Program.cs of every generated hub has to compile");
        patched
            .Should()
            .Contain(
                ".AddMediator(typeof(Program).Assembly, typeof(Demo.Trains.ManifestNames).Assembly)"
            );
    }

    [Test]
    public void PatchProgramCs_WithoutTheAssemblyScan_Throws()
    {
        File.WriteAllText(
            Path.Combine(_hubDir, "Program.cs"),
            "var builder = WebApplication.CreateBuilder(args);\nbuilder.Build().Run();\n"
        );

        var act = () => TraxProjectGenerator.PatchProgramCs(_hubDir, "Demo");

        act.Should()
            .Throw<InvalidOperationException>()
            .WithMessage(
                "*typeof(Program).Assembly*",
                "otherwise the hub silently registers no trains"
            );
    }

    [Test]
    public void PatchProgramCs_WithoutAProgramFile_Throws()
    {
        var act = () => TraxProjectGenerator.PatchProgramCs(_hubDir, "Demo");

        act.Should().Throw<InvalidOperationException>().WithMessage("*Program.cs*");
    }

    private static string TemplateProgram =>
        Path.Combine(
            TestContext.CurrentContext.TestDirectory,
            "Fixtures",
            "HubTemplate",
            "Program.cs.txt"
        );
}
