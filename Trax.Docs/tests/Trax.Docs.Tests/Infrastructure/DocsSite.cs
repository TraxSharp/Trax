namespace Trax.Docs.Tests.Infrastructure;

/// <summary>
/// The docs as traxsharp.net serves them: the published pages, the old paths their
/// <c>redirect_from</c> front matter claims, and the heading ids on each page.
/// </summary>
internal sealed class DocsSite
{
    private readonly Dictionary<string, HashSet<string>> _headingIds;

    private DocsSite(
        HashSet<string> pages,
        IReadOnlyList<(string Page, string Path)> redirects,
        Dictionary<string, HashSet<string>> headingIds
    )
    {
        _headingIds = headingIds;
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
    ) =>
        new(
            pages.ToHashSet(StringComparer.Ordinal),
            redirects.ToList(),
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal)
        );

    /// <summary>
    /// Whether a page is an SDK reference leaf page, one that documents a member or type:
    /// anything under <c>sdk-reference/&lt;dir&gt;/</c>. <c>sdk-reference.md</c> and
    /// <c>sdk-reference/&lt;dir&gt;.md</c> are the index pages of the section and its folders.
    /// </summary>
    public static bool IsSdkReferenceLeaf(string page) =>
        page.StartsWith("sdk-reference/", StringComparison.Ordinal) && page.Split('/').Length >= 3;

    /// <summary>
    /// The site this checkout publishes. Reads every published page once, for its redirect
    /// claims and its heading ids.
    /// </summary>
    public static async Task<DocsSite> LoadAsync()
    {
        var pages = RepoRoot.PublishedPages().ToHashSet(StringComparer.Ordinal);
        var redirects = new List<(string Page, string Path)>();
        var headingIds = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var page in pages.Order(StringComparer.Ordinal))
        {
            var text = await File.ReadAllTextAsync(RepoRoot.Combine(page.Split('/')))
                .ConfigureAwait(false);
            redirects.AddRange(FrontMatter.Of(text).List("redirect_from").Select(r => (page, r)));
            headingIds[page] = Headings.Ids(text);
        }
        return new DocsSite(pages, redirects, headingIds);
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
        string.Equals(page, "index.md", StringComparison.Ordinal)
            ? "/docs"
            : "/docs/" + page[..^".md".Length];

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

    /// <summary>The heading ids on a published page; empty for a page the site does not publish.</summary>
    public IReadOnlySet<string> HeadingIds(string page) =>
        _headingIds.TryGetValue(page, out var ids)
            ? ids
            : new HashSet<string>(StringComparer.Ordinal);

    private static string Normalize(string path) => path.Length > 1 ? path.TrimEnd('/') : path;
}
