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

    private static readonly Dictionary<string, string> Chapters = new()
    {
        ["two.md"] = "one.md",
        ["three.md"] = "two.md",
    };

    private static List<SnippetUnit> Book(params (string Page, string Markdown)[] pages) =>
        SnippetUnit
            .Following(
                SnippetUnit.Own(pages.SelectMany(p => Fences.In(p.Page, p.Markdown))).ToList(),
                page => Chapters.GetValueOrDefault(page)
            )
            .ToList();

    [Test]
    public void FollowingPage_CompilesWithTheFencesOfThePagesBeforeIt()
    {
        var units = Book(
            (
                "one.md",
                "```csharp compile=app file=Greeting.cs\npublic record Greeting(string Text);\n```\n"
            ),
            (
                "two.md",
                "```csharp compile=app file=Program.cs\nConsole.WriteLine(new Greeting(\"hi\").Text);\n```\n"
            )
        );

        var two = units.Single(u => u.Page == "two.md");
        two.Fences.Select(f => f.Page).Should().Equal("one.md", "two.md");
        SnippetCompiler.Diagnose(two).Should().BeEmpty();
    }

    [Test]
    public void FenceNamingAFile_ReplacesTheEarlierFenceForThatFile()
    {
        var units = Book(
            (
                "one.md",
                "```csharp compile=app file=Greeting.cs\npublic record Greeting(string Text);\n```\n"
            ),
            (
                "two.md",
                "```csharp compile=app file=Greeting.cs\npublic record Greeting(string Text, int Times);\n```\n"
            ),
            (
                "three.md",
                "```csharp compile=app file=Program.cs\nConsole.WriteLine(new Greeting(\"hi\", 2).Times);\n```\n"
            )
        );

        var three = units.Single(u => u.Page == "three.md");
        three.Fences.Select(f => f.Page).Should().Equal("two.md", "three.md");
        SnippetCompiler.Diagnose(three).Should().BeEmpty();
    }

    [Test]
    public void FollowedPage_KeepsOnlyItsOwnFences()
    {
        var units = Book(
            ("one.md", "```csharp compile=app file=A.cs\npublic record A;\n```\n"),
            ("two.md", "```csharp compile=app file=B.cs\npublic record B;\n```\n")
        );

        units.Single(u => u.Page == "one.md").Fences.Should().ContainSingle();
    }

    [Test]
    public void ChainThatComesBack_Throws()
    {
        var act = () => SnippetUnit.ChainOf("a.md", page => page == "a.md" ? "b.md" : "a.md");

        act.Should().Throw<InvalidOperationException>().WithMessage("*comes back to a.md*");
    }

    [Test]
    public void FileWord_IsReadFromTheInfoString()
    {
        var fence = Fences.In("page.md", "```csharp compile=app file=Trains/X.cs\n```\n").Single();

        fence.File.Should().Be("Trains/X.cs");
        fence.Markers.Should().Equal(Fences.Marker);
    }

    [TestCase(
        "---\ntitle: X\nfollows: book/an-order-is-a-train\n---\n",
        "book/an-order-is-a-train.md"
    )]
    [TestCase("---\nfollows: /book/one/  \n---\n", "book/one.md")]
    [TestCase("---\ntitle: X\n---\nfollows: book/one\n", null)]
    [TestCase("# No front matter\n", null)]
    public void Follows_IsReadFromTheFrontMatterOnly(string markdown, string? page) =>
        SnippetCompiler.FollowedPageIn(markdown).Should().Be(page);

    [Test]
    public void TestsGroup_CompilesWithTheAppAndTheTestFrameworks()
    {
        var units = SnippetUnit
            .WithTheAppInTheTests(
                SnippetUnit
                    .Own(
                        Fences.In(
                            "page.md",
                            "```csharp compile=app file=Greeting.cs\npublic record Greeting(string Text);\n```\n\n"
                                + "```csharp compile=tests file=tests/GreetingTests.cs\n"
                                + "using AwesomeAssertions;\nusing NUnit.Framework;\n\n"
                                + "public class GreetingTests\n{\n    [Test]\n    public void Text_IsKept() =>\n"
                                + "        new Greeting(\"hi\").Text.Should().Be(\"hi\");\n}\n```\n"
                        )
                    )
                    .ToList()
            )
            .ToList();

        var tests = units.Single(u => u.IsTests);
        tests.App.Should().ContainSingle();
        SnippetCompiler.Diagnose(tests).Should().BeEmpty();
    }

    [Test]
    public void TestsGroup_HasThePlainSdksImplicitUsings_NotTheWebSdks()
    {
        // A test project is a Microsoft.NET.Sdk project: ServiceCollection needs its using.
        var units = SnippetUnit
            .WithTheAppInTheTests(
                SnippetUnit
                    .Own(
                        Fences.In(
                            "page.md",
                            "```csharp compile=app\npublic record Greeting(string Text);\n```\n\n"
                                + "```csharp compile=tests\npublic class T\n{\n"
                                + "    public object S() => new ServiceCollection();\n}\n```\n"
                        )
                    )
                    .ToList()
            )
            .ToList();

        SnippetCompiler
            .Diagnose(units.Single(u => u.IsTests))
            .Should()
            .Contain(d => d.Contains("CS0246") && d.Contains("ServiceCollection"));
    }

    [Test]
    public void TestsGroup_SeesTheAppsInternals()
    {
        var units = SnippetUnit
            .WithTheAppInTheTests(
                SnippetUnit
                    .Own(
                        Fences.In(
                            "page.md",
                            "```csharp compile=app\ninternal record Secret(int Value);\n```\n\n"
                                + "```csharp compile=tests\npublic class T\n{\n"
                                + "    internal int S() => new Secret(1).Value;\n}\n```\n"
                        )
                    )
                    .ToList()
            )
            .ToList();

        SnippetCompiler.Diagnose(units.Single(u => u.IsTests)).Should().BeEmpty();
    }

    [Test]
    public void AppGroup_DoesNotSeeTheTestFrameworks()
    {
        var unit = Only("```csharp compile=app\nusing NUnit.Framework;\n\npublic class X;\n```\n");

        SnippetCompiler.Diagnose(unit).Should().Contain(d => d.Contains("CS0246"));
    }

    [Test]
    public void TestsGroup_OnAPageWithNoApp_TestsTheAppOfThePageItFollows()
    {
        var units = SnippetUnit
            .WithTheAppInTheTests(
                SnippetUnit
                    .Own(
                        Fences
                            .In(
                                "one.md",
                                "```csharp compile=app file=A.cs\npublic record A;\n```\n"
                            )
                            .Concat(
                                Fences.In(
                                    "two.md",
                                    "```csharp compile=tests file=T.cs\npublic class T { public A? Value; }\n```\n"
                                )
                            )
                    )
                    .ToList(),
                page => Chapters.GetValueOrDefault(page)
            )
            .ToList();

        var tests = units.Single(u => u.IsTests);
        tests.App.Should().ContainSingle();
        SnippetCompiler.Diagnose(tests).Should().BeEmpty();
    }

    [Test]
    public void MovedFiles_AreReplacedAtTheirNewPath()
    {
        var units = SnippetUnit
            .Following(
                SnippetUnit
                    .Own(
                        Fences
                            .In(
                                "one.md",
                                "```csharp compile=app file=Trains/A.cs\npublic record A;\n```\n"
                            )
                            .Concat(
                                Fences.In(
                                    "two.md",
                                    "```csharp compile=app file=Core/Trains/A.cs\npublic record A(int X);\n```\n"
                                )
                            )
                    )
                    .ToList(),
                page => Chapters.GetValueOrDefault(page),
                page => page == "two.md" ? [("Trains/", "Core/Trains/")] : []
            )
            .ToList();

        var two = units.Single(u => u.Page == "two.md");
        two.Fences.Should().ContainSingle().Which.Page.Should().Be("two.md");
        SnippetCompiler.Diagnose(two).Should().BeEmpty();
    }

    [TestCase(
        "---\nmoves:\n  - Trains/ -> Core/Trains/\n  - \"Program.cs -> Hub/Program.cs\"\ntitle: X\n---\n"
    )]
    public void Moves_AreReadFromTheFrontMatter(string markdown) =>
        SnippetCompiler
            .MovesIn(markdown)
            .Should()
            .Equal(("Trains/", "Core/Trains/"), ("Program.cs", "Hub/Program.cs"));

    [Test]
    public void AnotherGroupOnAChainedPage_CompilesAgainstTheApp()
    {
        var units = SnippetUnit
            .WithTheAppInTheTests(
                SnippetUnit
                    .Own(
                        Fences
                            .In(
                                "one.md",
                                "```csharp compile=app file=A.cs\npublic record A;\n```\n"
                            )
                            .Concat(
                                Fences.In(
                                    "two.md",
                                    "```csharp compile=worker file=Worker/Program.cs\nConsole.WriteLine(new A());\n```\n"
                                )
                            )
                    )
                    .ToList(),
                page => Chapters.GetValueOrDefault(page)
            )
            .ToList();

        var worker = units.Single(u => u.Group == "worker");
        worker.App.Should().ContainSingle();
        SnippetCompiler.Diagnose(worker).Should().BeEmpty();
    }
}
