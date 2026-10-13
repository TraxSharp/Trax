namespace Trax.Docs.Snippets.Tests.Infrastructure;

/// <summary>
/// The docs root (the folder holding <c>Trax.Docs.slnx</c>) and the repository root above it.
/// Page paths are relative to the first; <c>from=</c> paths, like <c>source:</c> front matter, to
/// the second.
/// </summary>
internal static class RepoRoot
{
    private static readonly Lazy<string> Cached = new(Resolve);

    public static string Path => Cached.Value;

    /// <summary>The repository root: the folder holding Trax.Docs and every Trax.* folder.</summary>
    public static string Monorepo => Directory.GetParent(Path)!.FullName;

    public static string Combine(params string[] segments) =>
        System.IO.Path.Combine(new[] { Path }.Concat(segments).ToArray());

    public static string Relative(string absolute) =>
        System.IO.Path.GetRelativePath(Path, absolute);

    /// <summary>
    /// Enumerates every .md file in the repo, skipping bin/obj/.git/node_modules.
    /// </summary>
    public static IEnumerable<string> MarkdownFiles()
    {
        foreach (var file in Directory.EnumerateFiles(Path, "*.md", SearchOption.AllDirectories))
        {
            if (IsExcluded(file))
                continue;
            yield return file;
        }
    }

    /// <summary>
    /// Whether traxsharp.net publishes the file at <paramref name="relative"/> (docs-relative,
    /// forward slashes) as a page. Mirrors the exclusions in Trax.Website's
    /// <c>scripts/sync-docs.sh</c>, as the same method in Trax.Docs.Tests does.
    /// </summary>
    public static bool IsPublished(string relative) =>
        System.IO.Path.GetFileName(relative) != "README.md"
        && !relative.StartsWith("adr/", StringComparison.Ordinal)
        && !relative.StartsWith(".claude/", StringComparison.Ordinal)
        && !relative.StartsWith("tools/", StringComparison.Ordinal)
        && !relative.StartsWith("tests/", StringComparison.Ordinal)
        && !relative.StartsWith(".github/", StringComparison.Ordinal);

    private static bool IsExcluded(string path)
    {
        var s = System.IO.Path.DirectorySeparatorChar;
        return path.Contains($"{s}bin{s}", StringComparison.Ordinal)
            || path.Contains($"{s}obj{s}", StringComparison.Ordinal)
            || path.Contains($"{s}.git{s}", StringComparison.Ordinal)
            || path.Contains($"{s}node_modules{s}", StringComparison.Ordinal);
    }

    private static string Resolve()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.EnumerateFiles("*.slnx").Any())
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            $"Could not locate repository root: no .slnx found walking up from '{AppContext.BaseDirectory}'."
        );
    }
}
