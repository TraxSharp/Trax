namespace Trax.Docs.Snippets.Tests.Tests;

/// <summary>
/// The matcher behind <see cref="SampleExcerptsMatchSourceTests"/>, run on files written here,
/// so a regression in it shows up as itself rather than as every excerpt going quietly green.
/// </summary>
[TestFixture]
public class ExcerptsTests
{
    private const string Program = """
        using Trax.Samples;

        var builder = WebApplication.CreateBuilder(args);

        #region docs-services
        builder.Services.AddTrax(trax =>
            trax.AddEffects(effects => effects.UsePostgres(connectionString))
                .AddMediator(typeof(Program).Assembly)
        );
        #endregion

        var app = builder.Build();
        app.MapGet("/", () => "ok");   
        app.Run();
        """;

    [Test]
    public void WholeFile_MatchesAnExactCopy() =>
        Excerpts.Match(Program, Program, region: null).Should().BeNull();

    [Test]
    public void Chunks_MatchInOrder_AcrossElisions() =>
        Excerpts
            .Match(
                "var builder = WebApplication.CreateBuilder(args);\n\n// ...\n\napp.Run();",
                Program,
                region: null
            )
            .Should()
            .BeNull();

    [Test]
    public void Chunks_OutOfOrder_Fail() =>
        Excerpts
            .Match(
                "app.Run();\n// ...\nvar builder = WebApplication.CreateBuilder(args);",
                Program,
                region: null
            )
            .Should()
            .Contain("chunk 2 of 2")
            .And.Contain("after the previous chunk")
            .And.Contain("var builder");

    [Test]
    public void ChangedLine_Fails_NamingTheChunk() =>
        Excerpts
            .Match("var app = builder.Build();\napp.MapGet(\"/\", () => \"OK\");", Program, null)
            .Should()
            .Contain("chunk 1 of 1 (fence line 1) does not occur in the file");

    [Test]
    public void IndentationAndTrailingWhitespace_AreNotCompared() =>
        Excerpts
            .Match(
                "trax.AddEffects(effects => effects.UsePostgres(connectionString))  \n"
                    + "    .AddMediator(typeof(Program).Assembly)",
                Program,
                null
            )
            .Should()
            .BeNull("a chunk lifted out of a nested call drops the indentation it had");

    [Test]
    public void RelativeIndentation_IsCompared() =>
        Excerpts
            .Match(
                "trax.AddEffects(effects => effects.UsePostgres(connectionString))\n"
                    + ".AddMediator(typeof(Program).Assembly)",
                Program,
                null
            )
            .Should()
            .NotBeNull();

    [Test]
    public void Region_SelectsItsLinesOnly()
    {
        Excerpts
            .Match("builder.Services.AddTrax(trax =>", Program, "docs-services")
            .Should()
            .BeNull();
        Excerpts
            .Match("app.Run();", Program, "docs-services")
            .Should()
            .Contain("region 'docs-services'");
    }

    [Test]
    public void RegionMarkers_AreNotPartOfTheText() =>
        Excerpts
            .Match(
                "var builder = WebApplication.CreateBuilder(args);\n\n"
                    + "builder.Services.AddTrax(trax =>",
                Program,
                null
            )
            .Should()
            .BeNull();

    [Test]
    public void MissingRegion_Fails() =>
        Excerpts
            .Match("app.Run();", Program, "docs-nope")
            .Should()
            .Contain("no '#region docs-nope'");

    [Test]
    public void UnclosedRegion_Fails() =>
        Excerpts
            .Match("x", "#region docs-open\nx\n", "docs-open")
            .Should()
            .Contain("no matching '#endregion'");

    [Test]
    public void NestedRegion_EndsAtItsOwnEndregion()
    {
        const string source =
            "#region docs-outer\na\n#region docs-inner\nb\n#endregion\nc\n#endregion\nd\n";

        Excerpts.Match("a\nb\nc", source, "docs-outer").Should().BeNull();
        Excerpts.Match("b", source, "docs-inner").Should().BeNull();
        Excerpts.Match("c", source, "docs-inner").Should().NotBeNull();
        Excerpts.Match("d", source, "docs-outer").Should().NotBeNull();
    }

    [TestCase("// #region docs-x\nconst x = 1;\n// #endregion\nconst y = 2;\n")]
    [TestCase("<!-- #region docs-x -->\nconst x = 1;\n<!-- #endregion -->\nconst y = 2;\n")]
    public void CommentedRegionMarkers_SelectTheirLines(string source)
    {
        Excerpts.Match("const x = 1;", source, "docs-x").Should().BeNull();
        Excerpts.Match("const y = 2;", source, "docs-x").Should().NotBeNull();
    }

    [TestCase("# ...")]
    [TestCase("<!-- ... -->")]
    [TestCase("  // ...  ")]
    public void ElisionLines_InEachLanguage_SplitChunks(string elision) =>
        Excerpts.Chunks($"a\n{elision}\nb").Select(c => c.Lines).Should().HaveCount(2);

    [Test]
    public void Chunks_DropBlankEnds_AndKeepTheirFenceLine()
    {
        var chunks = Excerpts.Chunks("\n  a\n    b\n\n// ...\n\n  c\n");

        chunks.Should().HaveCount(2);
        chunks[0].Start.Should().Be(1);
        chunks[0].Lines.Should().Equal("a", "  b");
        chunks[1].Start.Should().Be(6);
        chunks[1].Lines.Should().Equal("c");
    }

    [Test]
    public void EmptyFence_Fails() =>
        Excerpts.Match("\n// ...\n", Program, null).Should().NotBeNull();

    [TestCase("../etc/passwd", "relative to the repository root")]
    [TestCase("/Trax.Samples/x.cs", "relative to the repository root")]
    [TestCase("Trax.Samples/no/such/file.cs", "no such file")]
    public void Path_IsRepositoryRelative_AndExists(string from, string problem) =>
        Excerpts
            .Problem(new Fence("page.md", 3, "csharp", [], "x") { From = from })
            .Should()
            .StartWith($"page.md:3 from={from}: ")
            .And.Contain(problem);

    [Test]
    public void Path_NamesAFileUnderTheRepositoryRoot() =>
        Excerpts
            .Problem(
                new Fence("page.md", 1, "md", [], "# Trax.Docs") { From = "Trax.Docs/README.md" }
            )
            .Should()
            .BeNull();
}
