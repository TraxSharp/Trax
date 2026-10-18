namespace Trax.Docs.Snippets.Tests.Tests;

/// <summary>
/// A page that says <c>follows:</c> compiles with the fences of the page it names, a fence that
/// names a file replacing the earlier fence for that file (<see cref="SnippetUnit"/>). The book's
/// chapters use it to build one project across pages.
///
/// <para>Replacing by file only works when every fence of the chain says which file it is: an
/// unnamed fence in a chapter could never be replaced, and a later chapter showing the same file
/// would compile beside it. So a page in a chain names the file of every compiled fence, names
/// each file once per group, and follows a page that exists.</para>
///
/// <para>Enforces <c>Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md"
)]
[TestFixture]
public class FollowedPagesTests
{
    private const string Adr =
        "Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md";

    private static IReadOnlyList<string> Pages() =>
        RepoRoot
            .MarkdownFiles()
            .Select(f => RepoRoot.Relative(f).Replace('\\', '/'))
            .Where(RepoRoot.IsPublished)
            .ToList();

    [Test]
    public void Follows_NamesAPublishedPage_AndNoChainComesBack()
    {
        var pages = Pages().ToHashSet(StringComparer.Ordinal);
        var offenders = new List<string>();

        foreach (var page in pages)
        {
            var followed = SnippetCompiler.FollowedPageOf(page);
            if (followed is null)
                continue;
            if (!pages.Contains(followed))
                offenders.Add($"{page}  follows {followed}, which is not a published page");
            else
            {
                try
                {
                    SnippetUnit.ChainOf(page, SnippetCompiler.FollowedPageOf);
                }
                catch (InvalidOperationException ex)
                {
                    offenders.Add(ex.Message);
                }
            }
        }

        offenders
            .Should()
            .BeEmpty(
                "`follows:` names the page, relative to the docs root and without `.md`, whose "
                    + "project this page carries on. See "
                    + Adr
                    + ". Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    [Test]
    public void FileWord_IsNotOnAnUncompiledCSharpFence()
    {
        var offenders = Fences
            .OnPublishedPages()
            .Where(f => f.File is not null && f.IsCSharp && !f.IsCompiled)
            .Select(f => f.Location)
            .ToList();

        offenders
            .Should()
            .BeEmpty(
                "`file=` says which file of the project a fence is. A C# fence that is a file "
                    + "of the project compiles, so `file=` on a C# fence goes beside `compile`; a "
                    + "fragment or an excerpt is not a file of the project. See "
                    + Adr
                    + ". Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    [Test]
    public void PageInAChain_NamesTheFileOfEveryCompiledFence_OncePerGroup()
    {
        var pages = Pages();
        var inChain = pages
            .Where(p => SnippetCompiler.FollowedPageOf(p) is not null)
            .Concat(pages.Select(SnippetCompiler.FollowedPageOf).OfType<string>())
            .ToHashSet(StringComparer.Ordinal);

        var units = SnippetUnit
            .Own(Fences.OnPublishedPages())
            .Where(u => inChain.Contains(u.Page))
            .ToList();

        var unnamed = units
            .SelectMany(u => u.Fences)
            .Where(f => f.File is null)
            .Select(f => $"{f.Location}  names no file")
            .Distinct();
        var twice = units.SelectMany(u =>
            u.Fences.Where(f => f.File is not null)
                .GroupBy(f => f.File)
                .Where(g => g.Count() > 1)
                .Select(g => $"{u.Page} [{u.Group}]  names {g.Key} {g.Count()} times")
        );
        var offenders = unnamed.Concat(twice).ToList();

        offenders
            .Should()
            .BeEmpty(
                "a page that follows another, or is followed, builds one project with it, and a "
                    + "later page replaces a file by naming it. A fence that names no file could "
                    + "never be replaced, and a file named twice in one group is two copies of it. "
                    + "Give each compiled fence `file=<path>`. See "
                    + Adr
                    + ". Offenders:\n  "
                    + string.Join("\n  ", offenders)
            );
    }
}
