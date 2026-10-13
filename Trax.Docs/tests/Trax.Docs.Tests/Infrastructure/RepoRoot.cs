namespace Trax.Docs.Tests.Infrastructure;

internal static class RepoRoot
{
    private static readonly Lazy<string> Cached = new(Resolve);

    public static string Path => Cached.Value;

    /// <summary>
    /// The repository root: the folder holding Trax.Docs and every Trax.* folder. The paths in
    /// <c>source:</c> front matter and <c>from=</c> fences are relative to it.
    /// </summary>
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
    /// Whether traxsharp.net publishes the file at <paramref name="relative"/> (repo-relative,
    /// forward slashes) as a page. Mirrors the exclusions in Trax.Website's
    /// <c>scripts/sync-docs.sh</c>.
    /// </summary>
    public static bool IsPublished(string relative) =>
        System.IO.Path.GetFileName(relative) != "README.md"
        && !relative.StartsWith("adr/", StringComparison.Ordinal)
        && !relative.StartsWith(".claude/", StringComparison.Ordinal)
        && !relative.StartsWith("tools/", StringComparison.Ordinal)
        && !relative.StartsWith("tests/", StringComparison.Ordinal)
        && !relative.StartsWith(".github/", StringComparison.Ordinal);

    /// <summary>Every page traxsharp.net publishes, docs-relative with forward slashes, in ordinal order.</summary>
    public static IEnumerable<string> PublishedPages() =>
        MarkdownFiles()
            .Select(f => Relative(f).Replace('\\', '/'))
            .Where(IsPublished)
            .Order(StringComparer.Ordinal);

    /// <summary>Whether a file or directory exists at a repository-root-relative path.</summary>
    public static bool ExistsInMonorepo(string relative)
    {
        var full = System.IO.Path.Combine(
            new[] { Monorepo }.Concat(relative.TrimEnd('/').Split('/')).ToArray()
        );
        return File.Exists(full) || Directory.Exists(full);
    }

    /// <summary>
    /// The files matching <paramref name="pattern"/> under <paramref name="directory"/>, never
    /// descending into bin, obj, .git or node_modules.
    /// </summary>
    public static IEnumerable<string> FilesUnder(string directory, string pattern)
    {
        if (!Directory.Exists(directory))
            yield break;

        foreach (var file in Directory.EnumerateFiles(directory, pattern))
            yield return file;

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            if (System.IO.Path.GetFileName(child) is "bin" or "obj" or ".git" or "node_modules")
                continue;
            foreach (var file in FilesUnder(child, pattern))
                yield return file;
        }
    }

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
