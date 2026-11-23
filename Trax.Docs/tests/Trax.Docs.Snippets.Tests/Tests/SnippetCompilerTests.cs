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
            .GroupBy(x => x.Group, StringComparer.Ordinal)
            .Select(g => new SnippetUnit("page.md", g.Key, g.Select(x => x.Fence).ToList()))
            .ToList();

    [Test]
    public void UnmarkedFence_IsNotCompiled()
    {
        Units("```csharp\nthis is not C#\n```\n").Should().BeEmpty();
    }

    [Test]
    public async Task Error_NamesThePageAndTheLineOnIt()
    {
        var unit = Only(
            "# Title\n\nText.\n\n```csharp compile\nvar x = 1;\nx.NoSuchMethod();\n```\n"
        );

        (await SnippetCompiler.DiagnoseAsync(unit).ConfigureAwait(false))
            .Should()
            .ContainSingle()
            .Which.Should()
            .StartWith("page.md:7:")
            .And.Contain("CS1061");
    }

    [Test]
    public async Task Warning_FailsTheSnippet()
    {
        var unit = Only("```csharp compile\nstring s = null;\nConsole.WriteLine(s);\n```\n");

        (await SnippetCompiler.DiagnoseAsync(unit).ConfigureAwait(false))
            .Should()
            .Contain(d => d.Contains("warning CS8600"));
    }

    [Test]
    public async Task AssemblyVersionUnificationWarnings_AreSuppressedAsMSBuildDoes()
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

        (await SnippetCompiler.DiagnoseAsync(unit).ConfigureAwait(false)).Should().BeEmpty();
    }

    [Test]
    public async Task FencesInOneGroup_CompileTogether()
    {
        var unit = Only(
            "```csharp compile=app\npublic record Greeting(string Text);\n```\n\n"
                + "```csharp compile=app\nConsole.WriteLine(new Greeting(\"hi\").Text);\n```\n"
        );

        unit.Fences.Should().HaveCount(2);
        (await SnippetCompiler.DiagnoseAsync(unit).ConfigureAwait(false)).Should().BeEmpty();
    }

    [Test]
    public async Task FenceInSeveralGroups_JoinsEach()
    {
        var units = Units(
            "```csharp compile=one,two\npublic record Greeting(string Text);\n```\n\n"
                + "```csharp compile=one\nConsole.WriteLine(new Greeting(\"one\").Text);\n```\n\n"
                + "```csharp compile=two\nConsole.WriteLine(new Greeting(\"two\").Text);\n```\n"
        );

        units.Should().HaveCount(2);
        foreach (var u in units)
            (await SnippetCompiler.DiagnoseAsync(u).ConfigureAwait(false)).Should().BeEmpty();
    }

    [Test]
    public async Task IndentedFence_LosesItsIndentation()
    {
        var unit = Only(
            "1. Step:\n\n   ```csharp compile\n   var x = 1;\n   Console.WriteLine(x);\n   ```\n"
        );

        unit.Fences.Single().Code.Should().Be("var x = 1;\nConsole.WriteLine(x);");
        (await SnippetCompiler.DiagnoseAsync(unit).ConfigureAwait(false)).Should().BeEmpty();
    }

    [Test]
    public async Task TraxPackages_AreReferenced()
    {
        var unit = Only(
            "```csharp compile\nusing Trax.Core.Junction;\n\n"
                + "public class Hello : Junction<string, string>\n{\n"
                + "    public override Task<string> Run(string input) => Task.FromResult(input);\n}\n```\n"
        );

        (await SnippetCompiler.DiagnoseAsync(unit).ConfigureAwait(false)).Should().BeEmpty();
    }

    [Test]
    public async Task TraxNamespaces_AreNotImplied()
    {
        var unit = Only(
            "```csharp compile\npublic class Hello : Junction<string, string>\n{\n"
                + "    public override Task<string> Run(string input) => Task.FromResult(input);\n}\n```\n"
        );

        (await SnippetCompiler.DiagnoseAsync(unit).ConfigureAwait(false))
            .Should()
            .Contain(d => d.Contains("CS0246"));
    }

    [Test]
    public async Task SharedContext_GivesEverySnippetTheHost()
    {
        var unit = Only(
            "```csharp compile\nservices.AddLogging();\n"
                + "Console.WriteLine(connectionString + rabbitMqConnectionString);\n"
                + "Console.WriteLine(configuration[\"x\"] ?? assemblies[0].FullName);\n"
                + "await Task.Delay(1, ct);\nawait Task.Delay(1, cancellationToken);\n"
                + "app.Run();\n```\n"
        );

        (await SnippetCompiler.DiagnoseAsync(unit).ConfigureAwait(false)).Should().BeEmpty();
    }

    [Test]
    public async Task ProgramCs_DeclaringItsOwnHost_HidesTheShared()
    {
        // The shape of every Program.cs a page shows: a top-level `var builder` and `var app`
        // hide the shared members without a warning, as a local hides a member.
        var unit = Only(
            "```csharp compile\nvar builder = WebApplication.CreateBuilder(args);\n"
                + "var services = builder.Services;\nservices.AddLogging();\n"
                + "var app = builder.Build();\napp.Run();\n```\n"
        );

        (await SnippetCompiler.DiagnoseAsync(unit).ConfigureAwait(false)).Should().BeEmpty();
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

    private static readonly Dictionary<string, string> Chapters = new(StringComparer.Ordinal)
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
    public async Task FollowingPage_CompilesWithTheFencesOfThePagesBeforeIt()
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

        var two = units.Single(u => string.Equals(u.Page, "two.md", StringComparison.Ordinal));
        two.Fences.Select(f => f.Page).Should().Equal("one.md", "two.md");
        (await SnippetCompiler.DiagnoseAsync(two).ConfigureAwait(false)).Should().BeEmpty();
    }

    [Test]
    public async Task FenceNamingAFile_ReplacesTheEarlierFenceForThatFile()
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

        var three = units.Single(u => string.Equals(u.Page, "three.md", StringComparison.Ordinal));
        three.Fences.Select(f => f.Page).Should().Equal("two.md", "three.md");
        (await SnippetCompiler.DiagnoseAsync(three).ConfigureAwait(false)).Should().BeEmpty();
    }

    [Test]
    public void FollowedPage_KeepsOnlyItsOwnFences()
    {
        var units = Book(
            ("one.md", "```csharp compile=app file=A.cs\npublic record A;\n```\n"),
            ("two.md", "```csharp compile=app file=B.cs\npublic record B;\n```\n")
        );

        units
            .Single(u => string.Equals(u.Page, "one.md", StringComparison.Ordinal))
            .Fences.Should()
            .ContainSingle();
    }

    [Test]
    public void ChainThatComesBack_Throws()
    {
        var act = () =>
            SnippetUnit.ChainOf(
                "a.md",
                page => string.Equals(page, "a.md", StringComparison.Ordinal) ? "b.md" : "a.md"
            );

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
    public async Task TestsGroup_CompilesWithTheAppAndTheTestFrameworks()
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
        (await SnippetCompiler.DiagnoseAsync(tests).ConfigureAwait(false)).Should().BeEmpty();
    }

    [Test]
    public async Task TestsGroup_HasThePlainSdksImplicitUsings_NotTheWebSdks()
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

        (await SnippetCompiler.DiagnoseAsync(units.Single(u => u.IsTests)).ConfigureAwait(false))
            .Should()
            .Contain(d => d.Contains("CS0246") && d.Contains("ServiceCollection"));
    }

    [Test]
    public async Task TestsGroup_SeesTheAppsInternals()
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

        (await SnippetCompiler.DiagnoseAsync(units.Single(u => u.IsTests)).ConfigureAwait(false))
            .Should()
            .BeEmpty();
    }

    [Test]
    public async Task AppGroup_DoesNotSeeTheTestFrameworks()
    {
        var unit = Only("```csharp compile=app\nusing NUnit.Framework;\n\npublic class X;\n```\n");

        (await SnippetCompiler.DiagnoseAsync(unit).ConfigureAwait(false))
            .Should()
            .Contain(d => d.Contains("CS0246"));
    }

    [Test]
    public async Task TestsGroup_OnAPageWithNoApp_TestsTheAppOfThePageItFollows()
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
        (await SnippetCompiler.DiagnoseAsync(tests).ConfigureAwait(false)).Should().BeEmpty();
    }

    [Test]
    public async Task MovedFiles_AreReplacedAtTheirNewPath()
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
                page =>
                    string.Equals(page, "two.md", StringComparison.Ordinal)
                        ? [("Trains/", "Core/Trains/")]
                        : []
            )
            .ToList();

        var two = units.Single(u => string.Equals(u.Page, "two.md", StringComparison.Ordinal));
        two.Fences.Should().ContainSingle().Which.Page.Should().Be("two.md");
        (await SnippetCompiler.DiagnoseAsync(two).ConfigureAwait(false)).Should().BeEmpty();
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
    public async Task AnotherGroupOnAChainedPage_CompilesAgainstTheApp()
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

        var worker = units.Single(u => string.Equals(u.Group, "worker", StringComparison.Ordinal));
        worker.App.Should().ContainSingle();
        (await SnippetCompiler.DiagnoseAsync(worker).ConfigureAwait(false)).Should().BeEmpty();
    }
}
