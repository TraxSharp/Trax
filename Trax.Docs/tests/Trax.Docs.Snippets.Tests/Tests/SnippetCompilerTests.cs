namespace Trax.Docs.Snippets.Tests.Tests;

/// <summary>
/// The extractor and compiler behind <see cref="CompiledSnippetsTests"/>, run on pages written
/// here, so a regression in either shows up as itself rather than as a page going quietly green.
/// </summary>
[TestFixture]
public class SnippetCompilerTests
{
    private static SnippetUnit Only(string markdown) =>
        Units(markdown).Should().ContainSingle().Subject;

    private static List<SnippetUnit> Units(string markdown) =>
        Fences
            .In("page.md", markdown)
            .Where(f => f.IsCompiled)
            .SelectMany(f => f.Groups.Select(g => (Fence: f, Group: g)))
            .GroupBy(x => x.Group)
            .Select(g => new SnippetUnit("page.md", g.Key, g.Select(x => x.Fence).ToList()))
            .ToList();

    [Test]
    public void UnmarkedFence_IsNotCompiled()
    {
        Units("```csharp\nthis is not C#\n```\n").Should().BeEmpty();
    }

    [Test]
    public void Error_NamesThePageAndTheLineOnIt()
    {
        var unit = Only(
            "# Title\n\nText.\n\n```csharp compile\nvar x = 1;\nx.NoSuchMethod();\n```\n"
        );

        SnippetCompiler
            .Diagnose(unit)
            .Should()
            .ContainSingle()
            .Which.Should()
            .StartWith("page.md:7:")
            .And.Contain("CS1061");
    }

    [Test]
    public void Warning_FailsTheSnippet()
    {
        var unit = Only("```csharp compile\nstring s = null;\nConsole.WriteLine(s);\n```\n");

        SnippetCompiler.Diagnose(unit).Should().Contain(d => d.Contains("warning CS8600"));
    }

    [Test]
    public void AssemblyVersionUnificationWarnings_AreSuppressedAsMSBuildDoes()
    {
        // Npgsql.EntityFrameworkCore.PostgreSQL references an older
        // Microsoft.EntityFrameworkCore.Relational than the one referenced here, which raises
        // CS1702 unless it is suppressed, as MSBuild's default NoWarn does in every C# project.
        var unit = Only(
            "```csharp compile\nusing Microsoft.EntityFrameworkCore;\n\n"
                + "var options = new DbContextOptionsBuilder()\n"
                + "    .UseNpgsql(connectionString, npgsql => npgsql.CommandTimeout(30))\n"
                + "    .Options;\nConsole.WriteLine(options.Extensions.Count());\n```\n"
        );

        SnippetCompiler.Diagnose(unit).Should().BeEmpty();
    }

    [Test]
    public void FencesInOneGroup_CompileTogether()
    {
        var unit = Only(
            "```csharp compile=app\npublic record Greeting(string Text);\n```\n\n"
                + "```csharp compile=app\nConsole.WriteLine(new Greeting(\"hi\").Text);\n```\n"
        );

        unit.Fences.Should().HaveCount(2);
        SnippetCompiler.Diagnose(unit).Should().BeEmpty();
    }

    [Test]
    public void FenceInSeveralGroups_JoinsEach()
    {
        var units = Units(
            "```csharp compile=one,two\npublic record Greeting(string Text);\n```\n\n"
                + "```csharp compile=one\nConsole.WriteLine(new Greeting(\"one\").Text);\n```\n\n"
                + "```csharp compile=two\nConsole.WriteLine(new Greeting(\"two\").Text);\n```\n"
        );

        units.Should().HaveCount(2);
        units.Should().AllSatisfy(u => SnippetCompiler.Diagnose(u).Should().BeEmpty());
    }

    [Test]
    public void IndentedFence_LosesItsIndentation()
    {
        var unit = Only(
            "1. Step:\n\n   ```csharp compile\n   var x = 1;\n   Console.WriteLine(x);\n   ```\n"
        );

        unit.Fences.Single().Code.Should().Be("var x = 1;\nConsole.WriteLine(x);");
        SnippetCompiler.Diagnose(unit).Should().BeEmpty();
    }

    [Test]
    public void TraxPackages_AreReferenced()
    {
        var unit = Only(
            "```csharp compile\nusing Trax.Core.Junction;\n\n"
                + "public class Hello : Junction<string, string>\n{\n"
                + "    public override Task<string> Run(string input) => Task.FromResult(input);\n}\n```\n"
        );

        SnippetCompiler.Diagnose(unit).Should().BeEmpty();
    }

    [Test]
    public void TraxNamespaces_AreNotImplied()
    {
        var unit = Only(
            "```csharp compile\npublic class Hello : Junction<string, string>\n{\n"
                + "    public override Task<string> Run(string input) => Task.FromResult(input);\n}\n```\n"
        );

        SnippetCompiler.Diagnose(unit).Should().Contain(d => d.Contains("CS0246"));
    }

    [Test]
    public void SharedContext_GivesEverySnippetTheHost()
    {
        var unit = Only(
            "```csharp compile\nservices.AddLogging();\n"
                + "Console.WriteLine(connectionString + rabbitMqConnectionString);\n"
                + "Console.WriteLine(configuration[\"x\"] ?? assemblies[0].FullName);\n"
                + "await Task.Delay(1, ct);\nawait Task.Delay(1, cancellationToken);\n"
                + "app.Run();\n```\n"
        );

        SnippetCompiler.Diagnose(unit).Should().BeEmpty();
    }

    [Test]
    public void ProgramCs_DeclaringItsOwnHost_HidesTheShared()
    {
        // The shape of every Program.cs a page shows: a top-level `var builder` and `var app`
        // hide the shared members without a warning, as a local hides a member.
        var unit = Only(
            "```csharp compile\nvar builder = WebApplication.CreateBuilder(args);\n"
                + "var services = builder.Services;\nservices.AddLogging();\n"
                + "var app = builder.Build();\napp.Run();\n```\n"
        );

        SnippetCompiler.Diagnose(unit).Should().BeEmpty();
    }

    [Test]
    public void ContextFiles_LayerFromSharedToPage()
    {
        SnippetCompiler
            .ContextFilesFor("sdk-reference/scheduler-api/schedule.md")
            .Select(f => Path.GetRelativePath(SnippetCompiler.ContextRoot, f).Replace('\\', '/'))
            .Should()
            .StartWith(SnippetCompiler.SharedFile)
            .And.EndWith("sdk-reference/scheduler-api/schedule.cs");
    }

    [Test]
    public void SnippetContextNone_KeepsOnlyThePagesOwnFile()
    {
        SnippetCompiler
            .ContextFilesFor("getting-started.md")
            .Should()
            .NotContain(f => Path.GetFileName(f) == SnippetCompiler.SharedFile);
    }

    [TestCase("---\ntitle: X\nsnippet_context: none\n---\n# X\n", true)]
    [TestCase("---\nsnippet_context:   none  \n---\n", true)]
    [TestCase("---\ntitle: X\n---\nsnippet_context: none\n---\n", false)]
    [TestCase("---\nsnippet_context: shared\n---\n", false)]
    [TestCase("# No front matter\n", false)]
    public void SnippetContextNone_IsReadFromTheFrontMatterOnly(string markdown, bool optsOut) =>
        SnippetCompiler.OptsOutOfSharedContextIn(markdown).Should().Be(optsOut);
}
