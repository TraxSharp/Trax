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
            .Where(f => f.IsMarked)
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
}
