namespace Trax.Docs.Tests.Tests;

/// <summary>
/// An SDK reference page whose <c>source:</c> names C# files is linked from one of them: the
/// code it documents carries <c>/// &lt;seealso href="https://traxsharp.net/docs/&lt;page&gt;"&gt;</c>.
///
/// <para>A page points at its code through <c>source:</c>; this is the other direction, so a
/// reader in the code (or IntelliSense) reaches the page, and the developer changing the type
/// sees which page describes it. <see cref="CodeLinksToDocsResolveTests"/> holds each such link
/// to a page that exists.</para>
///
/// <para>The pages that predate the rule are listed in <c>KnownUnlinkedSdkPages.txt</c>, which
/// only shrinks.</para>
///
/// <para>Enforces <c>Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md"
)]
[TestFixture]
public class SourceLinksBackTests
{
    private const string Adr =
        "Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md";

    private const string KnownUnlinkedFile = "tests/Trax.Docs.Tests/KnownUnlinkedSdkPages.txt";

    [Test]
    public void Every_SdkReferencePage_IsLinkedFromItsSource()
    {
        var unlinked = RepoRoot
            .PublishedPages()
            .Where(DocsSite.IsSdkReferenceLeaf)
            .Where(page => IsUnlinked(page, FrontMatter.OfPage(page).List("source"), ReadSource))
            .ToHashSet(StringComparer.Ordinal);

        var known = Ratchet.Entries(KnownUnlinkedFile);
        var offenders = unlinked
            .Where(p => !known.Contains(p))
            .Order(StringComparer.Ordinal)
            .ToList();
        var stale = Ratchet.Stale(known, unlinked);

        offenders
            .Should()
            .BeEmpty(
                "the code an SDK reference page documents links back to it: one of the C# files "
                    + "in the page's `source:` carries `/// <seealso "
                    + "href=\"https://traxsharp.net/docs/<page path>\">SDK reference</seealso>` on "
                    + "the type the page covers. See "
                    + Adr
                    + $". Do not add the page to {KnownUnlinkedFile}. Not linked back:\n  "
                    + string.Join("\n  ", offenders)
            );

        stale
            .Should()
            .BeEmpty(
                $"{KnownUnlinkedFile} only shrinks: delete each line whose page is now linked "
                    + $"from its source, or no longer exists. See {Adr}. Stale:\n  "
                    + string.Join("\n  ", stale)
            );

        Ratchet
            .IsSortedAndUnique(known)
            .Should()
            .BeTrue($"{KnownUnlinkedFile} lists each page once, in ordinal order. See {Adr}.");
    }

    [Test]
    public void ALink_MustNameThePage_NotAPrefixOfIt()
    {
        const string page = "sdk-reference/scheduler-api/schedule.md";
        string? Source(string path) =>
            path switch
            {
                "a.cs" =>
                    "/// <seealso href=\"https://traxsharp.net/docs/sdk-reference/scheduler-api/schedule\">x</seealso>",
                "b.cs" =>
                    "/// https://traxsharp.net/docs/sdk-reference/scheduler-api/schedule-many",
                "c.cs" =>
                    "/// https://traxsharp.net/docs/sdk-reference/scheduler-api/schedule#signatures.",
                _ => null,
            };

        IsUnlinked(page, ["a.cs"], Source).Should().BeFalse();
        IsUnlinked(page, ["b.cs"], Source).Should().BeTrue();
        IsUnlinked(page, ["b.cs", "c.cs"], Source).Should().BeFalse();
        IsUnlinked(page, ["x.cs", "Some/Dir"], Source)
            .Should()
            .BeFalse("no listed C# file exists to carry a link");
        IsUnlinked(page, [], Source).Should().BeFalse();
    }

    /// <summary>
    /// Whether the page lists C# files in <c>source:</c>, at least one of them exists, and none
    /// of those links to the page. A page whose sources are missing is SdkReferenceFrontMatterTests'
    /// to report.
    /// </summary>
    private static bool IsUnlinked(
        string page,
        IEnumerable<string> sources,
        Func<string, string?> read
    )
    {
        var texts = sources
            .Where(s => s.EndsWith(".cs", StringComparison.Ordinal))
            .Select(read)
            .OfType<string>()
            .ToList();
        if (texts.Count == 0)
            return false;

        var link = new Regex(
            Regex.Escape("https://traxsharp.net" + DocsSite.PathOf(page)) + @"(?![A-Za-z0-9_/\-])"
        );
        return !texts.Any(link.IsMatch);
    }

    private static string? ReadSource(string relative)
    {
        var full = Path.Combine(new[] { RepoRoot.Monorepo }.Concat(relative.Split('/')).ToArray());
        return File.Exists(full) ? File.ReadAllText(full) : null;
    }
}
