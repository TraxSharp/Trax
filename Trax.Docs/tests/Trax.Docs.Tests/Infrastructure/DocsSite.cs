namespace Trax.Docs.Tests.Infrastructure;

/// <summary>
/// The docs as traxsharp.net serves them: the published pages, the old paths their
/// <c>redirect_from</c> front matter claims, and the heading ids on each page.
/// </summary>
internal sealed class DocsSite
{
    private readonly Dictionary<string, HashSet<string>> _headingIds = new(StringComparer.Ordinal);

    private DocsSite(HashSet<string> pages, IReadOnlyList<(string Page, string Path)> redirects)
    {
        Pages = pages;
        RedirectClaims = redirects;
        Redirects = redirects
            .GroupBy(r => Normalize(r.Path), StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Page, StringComparer.Ordinal);
    }

    /// <summary>Every published page, docs-relative.</summary>
    public IReadOnlySet<string> Pages { get; }

    /// <summary>Every <c>redirect_from</c> entry, as written, with the page that claims it.</summary>
    public IReadOnlyList<(string Page, string Path)> RedirectClaims { get; }

    /// <summary>Old <c>/docs/...</c> path (no trailing slash) to the page it now lands on.</summary>
    public IReadOnlyDictionary<string, string> Redirects { get; }

    /// <summary>A site of the given pages and redirect claims, for testing the rules on it.</summary>
    public static DocsSite Of(
        IEnumerable<string> pages,
        IEnumerable<(string Page, string Path)> redirects
    ) => new(pages.ToHashSet(StringComparer.Ordinal), redirects.ToList());

    /// <summary>
    /// Whether a page is an SDK reference leaf page, one that documents a member or type:
    /// anything under <c>sdk-reference/&lt;dir&gt;/</c>. <c>sdk-reference.md</c> and
    /// <c>sdk-reference/&lt;dir&gt;.md</c> are the index pages of the section and its folders.
    /// </summary>
    public static bool IsSdkReferenceLeaf(string page) =>
        page.StartsWith("sdk-reference/", StringComparison.Ordinal) && page.Split('/').Length >= 3;

    public static DocsSite Load()
    {
        var pages = RepoRoot.PublishedPages().ToHashSet(StringComparer.Ordinal);
        var redirects = pages
            .Order(StringComparer.Ordinal)
            .SelectMany(p => FrontMatter.OfPage(p).List("redirect_from").Select(r => (p, r)))
            .ToList();
        return new DocsSite(pages, redirects);
    }

    /// <summary>
    /// The file the site serves at a <c>/docs</c> path, published or not: <c>/docs</c> is
    /// <c>index.md</c> and <c>/docs/x</c> is <c>x.md</c>. Null for a path outside <c>/docs</c>.
    /// </summary>
    public static string? FileFor(string path)
    {
        if (path is "/docs" or "/docs/")
            return "index.md";
        if (path.StartsWith("/docs/", StringComparison.Ordinal))
            return path["/docs/".Length..].TrimEnd('/') + ".md";
        return null;
    }

    /// <summary>The <c>/docs</c> path a published page is served at.</summary>
    public static string PathOf(string page) =>
        page == "index.md" ? "/docs" : "/docs/" + page[..^".md".Length];

    /// <summary>
    /// The page a reader lands on at <paramref name="path"/>: the page itself, or the page whose
    /// <c>redirect_from</c> claims the path. Null when neither exists.
    /// </summary>
    public string? Resolve(string path)
    {
        if (FileFor(path) is { } file && Pages.Contains(file))
            return file;
        return Redirects.TryGetValue(Normalize(path), out var target) ? target : null;
    }

    public HashSet<string> HeadingIds(string page)
    {
        if (!_headingIds.TryGetValue(page, out var ids))
            _headingIds[page] = ids = Headings.Ids(
                File.ReadAllText(RepoRoot.Combine(page.Split('/')))
            );
        return ids;
    }

    private static string Normalize(string path) => path.Length > 1 ? path.TrimEnd('/') : path;
}
