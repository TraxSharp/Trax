namespace Trax.Docs.Tests.Tests;

/// <summary>
/// Every published page says which of the Diátaxis kinds it is, and which area of Trax it
/// belongs to.
///
/// <para>The <c>type</c> decides the sidebar section a page sits in and the badge above its
/// title; the <c>area</c> groups it with the other pages on the same subject, of every type. A
/// page without them falls out of both, and a page that is two types at once cannot say which,
/// so the field is what forces the split.</para>
///
/// <para>Enforces <c>Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md"
)]
[TestFixture]
public class FrontMatterTests
{
    private const string Adr =
        "Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md";

    /// <summary>The Diátaxis kinds, and <c>landing</c> for a page whose job is routing.</summary>
    public static readonly IReadOnlyList<string> Types =
    [
        "tutorial",
        "how-to",
        "reference",
        "explanation",
        "landing",
    ];

    /// <summary>The areas, in the order the sidebar shows them.</summary>
    public static readonly IReadOnlyList<string> Areas =
    [
        "general",
        "trains",
        "effects",
        "data",
        "mediator",
        "scheduling",
        "api",
        "security",
        "machines",
        "dashboard",
        "testing",
        "tooling",
        "upgrading",
        "samples",
        "contributing",
    ];

    [Test]
    public void EveryPublishedPage_HasATypeAndAnArea()
    {
        var offenders = RepoRoot
            .PublishedPages()
            .Select(page => (page, Problem: Problem(page, FrontMatter.OfPage(page))))
            .Where(x => x.Problem is not null)
            .Select(x => $"{x.page}  -> {x.Problem}")
            .ToList();

        offenders
            .Should()
            .BeEmpty(
                $"every published page carries `type:` (one of {string.Join(", ", Types)}) and "
                    + $"`area:` (one of {string.Join(", ", Areas)}) in its front matter, and an "
                    + "sdk-reference page below the section indexes is `type: reference`. Pick "
                    + "the one type the page is; a page that is two is two pages. See "
                    + Adr
                    + " and /docs/contributing/writing-docs. Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    [TestCase("---\ntitle: X\ntype: how-to\narea: scheduling\n---\n", null)]
    [TestCase("---\ntype: \"reference\"\narea: 'api'   \n---\n", null)]
    [TestCase("---\ntype: landing # the home page\narea: general\n---\n", null)]
    [TestCase("---\ntitle: X\narea: api\n---\n", "no type")]
    [TestCase("---\ntype: guide\narea: api\n---\n", "type 'guide' is not one of")]
    [TestCase("---\ntype: How-To\narea: api\n---\n", "type 'How-To' is not one of")]
    [TestCase("---\ntype: how-to\n---\n", "no area")]
    [TestCase("---\ntype: how-to\narea: scheduler\n---\n", "area 'scheduler' is not one of")]
    [TestCase("---\ntype: how-to\narea: [api, security]\n---\n", "area is a list")]
    [TestCase("# No front matter\n", "no front matter")]
    public void Problem_ReadsTypeAndArea(string markdown, string? expected)
    {
        var problem = Problem("page.md", FrontMatter.Of(markdown));

        if (expected is null)
            problem.Should().BeNull();
        else
            problem.Should().StartWith(expected);
    }

    [Test]
    public void SdkReferenceLeafPage_IsReference()
    {
        const string howTo = "---\ntype: how-to\narea: scheduling\n---\n";

        Problem("sdk-reference/scheduler-api/schedule.md", FrontMatter.Of(howTo))
            .Should()
            .Contain("type: reference");
        Problem("sdk-reference/scheduler-api.md", FrontMatter.Of(howTo)).Should().BeNull();
    }

    private static string? Problem(string page, FrontMatter frontMatter)
    {
        if (!frontMatter.Exists)
            return "no front matter";

        var type = frontMatter.Scalar("type");
        if (type is null)
            return frontMatter.Has("type") ? "type is a list or empty; give one" : "no type";
        if (!Types.Contains(type, StringComparer.Ordinal))
            return $"type '{type}' is not one of {string.Join(", ", Types)}";

        var area = frontMatter.Scalar("area");
        if (area is null)
            return frontMatter.Has("area")
                ? "area is a list or empty; a page has one area"
                : "no area";
        if (!Areas.Contains(area, StringComparer.Ordinal))
            return $"area '{area}' is not one of {string.Join(", ", Areas)}";

        if (DocsSite.IsSdkReferenceLeaf(page) && type != "reference")
            return $"an sdk-reference page is `type: reference`, not '{type}'";

        return null;
    }
}
