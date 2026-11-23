namespace Trax.Adr.Guard.Tests.Tests;

/// <summary>
/// Corpora and sources that are legal but unusual: a number used twice, an index row
/// written twice, two classes sharing a name, a wrapped attribute, a nested fence.
/// </summary>
[TestFixture]
public class GuardEdgeCaseTests
{
    private const string CensusRoot = "tests/Some.Tests.Meta";

    private static readonly AdrSpec Twin = new(
        "0001",
        "migrations-are-hand-written-sql",
        "Migrations are hand-written SQL",
        ["migrations"],
        ["effect"]
    );

    [Test]
    public async Task Run_TwoAdrsSharingANumber_ReportsTheDuplicate_InsteadOfThrowing()
    {
        using var repo = new TempAdrRepo();
        repo.Adr(Sample.DefaultSpec.FileName, Sample.Adr(spec: Sample.DefaultSpec));
        repo.Adr(Twin.FileName, Sample.Adr(spec: Twin));
        repo.Index(Sample.Index(central: false, Sample.DefaultSpec, Twin));

        var run = () => GuardRunner.RunAsync(repo.Options());

        var results = (await run.Should().NotThrowAsync().ConfigureAwait(false)).Which;
        results
            .Single(r => string.Equals(r.Name, "frontmatter/file-names", StringComparison.Ordinal))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("already used");
    }

    /// <summary>
    /// A check that throws must not take the report with it. The run fails either way; what
    /// matters is that the other checks, one of which usually names the real problem, still
    /// print.
    /// </summary>
    [Test]
    public void Guarded_ACheckThatThrows_FailsUnderItsOwnName()
    {
        var result = GuardRunner.Guarded(
            "index/full-list",
            () => throw new InvalidOperationException("boom")
        );

        result.Passed.Should().BeFalse();
        result.Name.Should().Be("index/full-list");
        result.Offenders.Should().ContainSingle().Which.Should().Contain("boom");
    }

    [Test]
    public async Task TagTable_SameFileNameInASubfolder_ReportsIt_InsteadOfThrowing()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Write(
            $"docs/adr/archive/{Sample.DefaultSpec.FileName}",
            Sample.Adr(spec: Sample.DefaultSpec)
        );

        var adrs = await AdrCorpus.DiscoverAsync(repo.Options()).ConfigureAwait(false);
        var check = () =>
            IndexGuards.TagTableAsync(adrs, repo.Options(), IndexGuards.ByAreaHeading, "areas");

        await check.Should().NotThrowAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// "The full table lists every ADR exactly once." A second row for the same ADR is
    /// parsed into the same dictionary slot, so the last row silently wins and the first,
    /// whatever it says, is never checked.
    /// </summary>
    [Test]
    public async Task FullList_AdrListedTwice_OnceWithAWrongTitle_Fails()
    {
        using var repo = TempAdrRepo.Valid();
        var spec = Sample.DefaultSpec;
        var index = Sample
            .Index(central: false, spec)
            .Replace(
                $"| {spec.Link} | {spec.Title} |",
                $"| {spec.Link} | A title nobody wrote |"
                    + " testing |\n"
                    + $"| {spec.Link} | {spec.Title} |",
                StringComparison.Ordinal
            );
        repo.Index(index);

        var result = await IndexGuards
            .FullListAsync(
                (await AdrCorpus.DiscoverAsync(repo.Options()).ConfigureAwait(false)),
                repo.Options()
            )
            .ConfigureAwait(false);

        result.Passed.Should().BeFalse();
        result.Offenders.Should().Contain(o => o.Contains($"{spec.FileName} is listed 2 times"));
        result.Offenders.Should().Contain(o => o.Contains("A title nobody wrote"));
    }

    /// <summary>
    /// Resolution and cite-back were moved off bare names because a name can be declared
    /// twice. The census still credits by name, so a second class sharing a claimed name
    /// is counted as classified although no ADR names it and it opts out of nothing.
    /// </summary>
    [Test]
    public async Task Census_SecondClassSharingAClaimedName_IsNotCreditedByTheOthersClaim()
    {
        using var repo = TempAdrRepo.Valid();
        var file = Sample.DefaultSpec.FileName;
        repo.Adr(
            file,
            Sample.Adr(
                exemplars: "`HttpRunExecutorTests` pins the request shape; it covers nothing else."
            )
        );
        repo.Write(
            $"{CensusRoot}/Request/HttpRunExecutorTests.cs",
            $$"""
            namespace Some.Tests.Meta.Request;

            /// <summary>Pins the request shape, per docs/adr/{{file}}.</summary>
            [Property("adr", "docs/adr/{{file}}")]
            [TestFixture]
            public class HttpRunExecutorTests
            {
                [Test]
                public void Shape() => Assert.Fail("docs/adr/{{file}}");
            }
            """
        );
        repo.Write(
            $"{CensusRoot}/Response/HttpRunExecutorTests.cs",
            """
            namespace Some.Tests.Meta.Response;

            [TestFixture]
            public class HttpRunExecutorTests
            {
                [Test]
                public void Mapping() => Assert.Pass();
            }
            """
        );

        var result = CensusGuards.EveryGuardIsClassified(
            (await AdrCorpus.DiscoverAsync(repo.Options()).ConfigureAwait(false)),
            repo.Options(censusRoot: CensusRoot)
        );

        result
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("Response/HttpRunExecutorTests.cs");
    }

    /// <summary>
    /// csharpier wraps a long attribute one argument per line, which the exemplar scan now
    /// joins. The census reads the docstring by walking up from the class and stops at the
    /// first line that is not '///', blank or '[', so the wrapped attribute's continuation
    /// lines cut the docstring off and a guard that cites its ADR is reported unclassified.
    /// </summary>
    [Test]
    public async Task Census_DocstringAboveAWrappedAttribute_StillCreditsTheCitation()
    {
        using var repo = TempAdrRepo.Valid();
        repo.Write(
            $"{CensusRoot}/WrappedTests.cs",
            """
            namespace Some.Tests.Meta;

            /// <summary>Enforces Trax.Docs/adr/0003-test-conventions.md.</summary>
            [TestFixture]
            [Property(
                "adr",
                "Trax.Docs/adr/0003-test-conventions-and-a-long-slug-that-wraps.md"
            )]
            public class WrappedTests
            {
                [Test]
                public void Holds() => Assert.Pass();
            }
            """
        );

        CensusGuards
            .EveryGuardIsClassified(
                (await AdrCorpus.DiscoverAsync(repo.Options()).ConfigureAwait(false)),
                repo.Options(censusRoot: CensusRoot)
            )
            .Offenders.Should()
            .BeEmpty();
    }

    /// <summary>
    /// CommonMark closes a fence only with a run at least as long as the opener. A
    /// four-backtick block quoting a three-backtick one is how an ADR shows a template that
    /// itself contains code, and the inner opener must not close the outer block.
    /// </summary>
    [Test]
    public void Section_HeadingInsideAFourBacktickFence_IsNotMistakenForTheRealOne()
    {
        const string text = """
            # A decision

            An ADR that shows how to quote the template:

            ````md
            ```md
            ## Exemplars

            **Unenforced:** the quoted example, which is not this document's answer.
            ```
            ````

            ## Status

            **Accepted.**
            """;

        Markdown.Section(text, "Exemplars").Should().BeNull();
    }

    /// <summary>
    /// A preprocessor line is prose, not code. An apostrophe in a region name read as an
    /// opening character literal blanked the rest of the file, and every declaration and
    /// citation after it disappeared from the scan.
    /// </summary>
    [Test]
    public void Blanking_AnApostropheInARegionName_DoesNotSwallowTheFile()
    {
        const string source = """
            #region The connection's operations
            public class LaterTests { }
            #endregion
            """;

        CSharp.WithoutCommentsAndLiterals(source).Should().Contain("public class LaterTests");
    }

    #region A claimed guard must run, and its message must carry the ADR

    private const string ProbePath = "tests/Some.Tests.Meta/ProbeTests.cs";

    private static TempAdrRepo WithClaimedProbe(string body, string classAttributes = "")
    {
        var repo = TempAdrRepo.Valid();
        var file = Sample.DefaultSpec.FileName;
        repo.Adr(file, Sample.Adr(exemplars: "- `ProbeTests` holds it up."));
        repo.Write(
            ProbePath,
            $$"""
            namespace Some.Tests.Meta;

            /// <summary>Enforces docs/adr/{{file}}.</summary>
            [Property("adr", "docs/adr/{{file}}")]
            [TestFixture{{classAttributes}}]
            public class ProbeTests
            {
            {{body}}
            }
            """
        );
        return repo;
    }

    private static async Task<GuardResult> ResolveAsync(TempAdrRepo repo) =>
        await ExemplarGuards
            .NamedGuardsResolveAsync(
                (await AdrCorpus.DiscoverAsync(repo.Options()).ConfigureAwait(false)),
                repo.Options()
            )
            .ConfigureAwait(false);

    private static async Task<GuardResult> CiteBackAsync(TempAdrRepo repo) =>
        await ExemplarGuards
            .NamedGuardsCiteBackAsync(
                (await AdrCorpus.DiscoverAsync(repo.Options()).ConfigureAwait(false)),
                repo.Options()
            )
            .ConfigureAwait(false);

    /// <summary>
    /// The probe from the audit: an explicit fixture with no tests, whose only "citation" is a
    /// constant nothing reads. It satisfied both resolution and cite-back.
    /// </summary>
    [Test]
    public async Task ExplicitClassWithOnlyAConstant_FailsResolutionAndCiteBack()
    {
        using var repo = WithClaimedProbe(
            $"    private const string Adr = \"docs/adr/{Sample.DefaultSpec.FileName}\";",
            ", Explicit"
        );

        (await ResolveAsync(repo).ConfigureAwait(false))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("[Explicit]");
        (await CiteBackAsync(repo).ConfigureAwait(false))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("does not name it in a failure message");
    }

    [Test]
    public async Task ClassWithNoTestMethod_FailsResolution()
    {
        using var repo = WithClaimedProbe(
            $"    public void Check() => Assert.Fail(\"docs/adr/{Sample.DefaultSpec.FileName}\");"
        );

        (await ResolveAsync(repo).ConfigureAwait(false))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("declares no [Test]");
    }

    [Test]
    public async Task ClassWhoseEveryTestIsIgnored_FailsResolution()
    {
        using var repo = WithClaimedProbe(
            $$"""
                [Test, Ignore("later")]
                public void Check() => Assert.Fail("docs/adr/{{Sample.DefaultSpec.FileName}}");
            """
        );

        (await ResolveAsync(repo).ConfigureAwait(false))
            .Offenders.Should()
            .ContainSingle()
            .Which.Should()
            .Contain("every test it declares is marked");
    }

    [Test]
    public async Task TestWhoseFailureMessageNamesTheAdr_Passes()
    {
        using var repo = WithClaimedProbe(
            $$"""
                [Test]
                public void Check() => Assert.Fail("docs/adr/{{Sample.DefaultSpec.FileName}}");
            """
        );

        (await ResolveAsync(repo).ConfigureAwait(false)).Offenders.Should().BeEmpty();
        (await CiteBackAsync(repo).ConfigureAwait(false)).Passed.Should().BeTrue();
    }

    /// <summary>
    /// The shape most real guards use: the citation held in a constant the assertion's
    /// 'because' reads, directly or through an interpolation hole.
    /// </summary>
    [TestCase("result.Should().BeTrue(\"it holds, per \" + Adr);")]
    [TestCase("result.Should().BeTrue($\"it holds ({Adr})\");")]
    public async Task ConstantAnAssertionReads_CountsAsTheCitation(string assertion)
    {
        using var repo = WithClaimedProbe(
            $$"""
                private const string Adr = "docs/adr/{{Sample.DefaultSpec.FileName}}";

                [Test]
                public void Check()
                {
                    var result = true;
                    {{assertion}}
                }
            """
        );

        (await CiteBackAsync(repo).ConfigureAwait(false)).Passed.Should().BeTrue();
    }

    [Test]
    public async Task ConstantNoAssertionReads_DoesNotCount()
    {
        using var repo = WithClaimedProbe(
            $$"""
                private const string Adr = "docs/adr/{{Sample.DefaultSpec.FileName}}";

                [Test]
                public void Check() => true.Should().BeTrue("it holds");
            """
        );

        (await CiteBackAsync(repo).ConfigureAwait(false)).Passed.Should().BeFalse();
    }

    /// <summary>
    /// csharpier wraps a long attribute one argument per line, which left the ADR path alone
    /// on a line that read as code and stood in for the failure message.
    /// </summary>
    [Test]
    public async Task WrappedAttributeArgument_IsNotAFailureMessage()
    {
        using var repo = TempAdrRepo.Valid();
        var file = Sample.DefaultSpec.FileName;
        repo.Adr(file, Sample.Adr(exemplars: "- `ProbeTests` holds it up."));
        repo.Write(
            ProbePath,
            $$"""
            namespace Some.Tests.Meta;

            /// <summary>Enforces docs/adr/{{file}}.</summary>
            [Property(
                "adr",
                "docs/adr/{{file}}"
            )]
            [TestFixture]
            public class ProbeTests
            {
                [Test]
                public void Check() => true.Should().BeTrue("it holds");
            }
            """
        );

        (await ResolveAsync(repo).ConfigureAwait(false)).Offenders.Should().BeEmpty();
        (await CiteBackAsync(repo).ConfigureAwait(false)).Passed.Should().BeFalse();
    }

    #endregion
}
