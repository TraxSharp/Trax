namespace Trax.Docs.Snippets.Tests.Tests;

/// <summary>
/// Every C# fence on a published page says how it relates to the code: <c>compile</c> (it
/// builds), <c>from=path[#region]</c> (it is an excerpt of that file) or <c>fragment</c> (it is
/// partial on purpose). Exactly one of them, and never <c>compile</c> on a signature.
///
/// <para>Unmarked, a fence reads the same whether it was checked yesterday or drifted from the
/// API a year ago, and nobody can tell which by looking. Requiring the marker makes the author
/// choose, and the choice is what the other guards hold the fence to.</para>
///
/// <para>The pages that predate the rule are listed in <c>KnownUnmarkedFencePages.txt</c>, which
/// only shrinks: a listed page with no unmarked fence left fails until its line is deleted.</para>
///
/// <para>Enforces <c>Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md"
)]
[TestFixture]
public class EveryCSharpFenceIsMarkedTests
{
    private const string Adr =
        "Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md";

    private const string KnownUnmarkedFile =
        "tests/Trax.Docs.Snippets.Tests/KnownUnmarkedFencePages.txt";

    [Test]
    public void Every_CSharpFence_OnAPublishedPage_IsMarked()
    {
        var unmarked = Fences
            .OnPublishedPages()
            .Where(f => f.IsCSharp && f.Markers.Count == 0)
            .ToList();
        var pages = unmarked.Select(f => f.Page).ToHashSet(StringComparer.Ordinal);
        var known = KnownUnmarked();

        var offenders = unmarked
            .Where(f => !known.Contains(f.Page))
            .Select(f => $"{f.Location}  (```{f.Language})")
            .ToList();

        var stale = known
            .Where(p => !pages.Contains(p))
            .Select(p =>
                File.Exists(RepoRoot.Combine(p.Split('/')))
                    ? $"{p}  (no unmarked fence left)"
                    : $"{p}  (no such page)"
            )
            .ToList();

        offenders
            .Should()
            .BeEmpty(
                "every C# fence on a published page carries `compile` (it builds in CI), "
                    + "`from=<path>[#region]` (it is an excerpt of that file, checked against it) or "
                    + "`fragment` (deliberately partial: a signature, a before and after, a line out "
                    + $"of context). See {Adr} and /docs/contributing/writing-docs. Do not add the "
                    + $"page to {KnownUnmarkedFile}. Unmarked:\n  "
                    + string.Join("\n  ", offenders)
            );

        stale
            .Should()
            .BeEmpty(
                $"{KnownUnmarkedFile} only shrinks: delete each line whose page has no unmarked "
                    + $"fence left, or no longer exists. See {Adr}. Stale:\n  "
                    + string.Join("\n  ", stale)
            );
    }

    [Test]
    public void KnownUnmarkedFencePages_IsSortedAndUnique()
    {
        var lines = Entries();

        lines
            .Should()
            .Equal(
                lines.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal),
                $"{KnownUnmarkedFile} lists each page once, in ordinal order, so two branches "
                    + $"that each delete a line merge cleanly. See {Adr}."
            );
    }

    [Test]
    public void Fence_CarriesAtMostOneMarker()
    {
        var offenders = Fences
            .OnPublishedPages()
            .Where(f => f.Markers.Count > 1)
            .Select(f => $"{f.Location}  ({string.Join(", ", f.Markers)})")
            .ToList();

        offenders
            .Should()
            .BeEmpty(
                "`compile`, `from=` and `fragment` are three different claims about a fence (it "
                    + "builds; it is copied from a file; it is partial), so a fence makes one. An "
                    + "excerpt that should also build belongs in the file it is taken from, which "
                    + $"its own project compiles. See {Adr}. Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    [Test]
    public void SignatureFence_IsNotCompiled()
    {
        var offenders = Fences
            .OnPublishedPages()
            .Where(f => f.IsCompiled && f.IsUnderSignature)
            .Select(f => $"{f.Location}  (under ## {f.Section})")
            .ToList();

        offenders
            .Should()
            .BeEmpty(
                "a fence under `## Signature` or `## Signatures` shows a declaration as the "
                    + "package declares it, which does not compile on its own; mark it `fragment` "
                    + "and put the compiled example under `## Example`. "
                    + "SdkReferenceFrontMatterTests checks the names it declares against the "
                    + $"package's public API baseline instead. See {Adr}. Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    [Test]
    public void Markers_AreReadFromTheInfoString()
    {
        var fences = Fences
            .In(
                "page.md",
                "## Signature\n\n```csharp fragment\npublic void Run();\n```\n\n"
                    + "### Overload\n\n```csharp compile\nvar x = 1;\n```\n\n"
                    + "## Example\n\n"
                    + "```csharp from=Trax.Samples/samples/X/Program.cs#docs-setup\nvar y = 2;\n```\n\n"
                    + "```json from=Trax.Samples/samples/X/appsettings.json\n{}\n```\n\n"
                    + "```csharp compile fragment\nvar z = 3;\n```\n\n"
                    + "```csharp\nvar w = 4;\n```\n"
            )
            .ToList();

        fences.Select(f => f.Markers.Count).Should().Equal(1, 1, 1, 1, 2, 0);

        fences[0].IsFragment.Should().BeTrue();
        fences[0].IsUnderSignature.Should().BeTrue();
        fences[1].IsCompiled.Should().BeTrue();
        fences[1].IsUnderSignature.Should().BeTrue("a level 3 heading stays inside its section");
        fences[2].From.Should().Be("Trax.Samples/samples/X/Program.cs");
        fences[2].FromRegion.Should().Be("docs-setup");
        fences[2].IsUnderSignature.Should().BeFalse();
        fences[3].Language.Should().Be("json");
        fences[3].From.Should().Be("Trax.Samples/samples/X/appsettings.json");
        fences[3].FromRegion.Should().BeNull();
        fences[4].Markers.Should().Equal(Fences.Marker, Fences.FragmentMarker);
    }

    private static List<string> Entries() =>
        File.ReadAllLines(RepoRoot.Combine(KnownUnmarkedFile.Split('/')))
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToList();

    private static HashSet<string> KnownUnmarked() => Entries().ToHashSet(StringComparer.Ordinal);
}
