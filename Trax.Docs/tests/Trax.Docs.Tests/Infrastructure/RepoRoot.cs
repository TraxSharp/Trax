namespace Trax.Docs.Tests.Infrastructure;

internal static class RepoRoot
{
    private static readonly Lazy<string> Cached = new(Resolve);

    public static string Path => Cached.Value;

    public static string Combine(params string[] segments) =>
        System.IO.Path.Combine(new[] { Path }.Concat(segments).ToArray());

    public static string Relative(string absolute) =>
        System.IO.Path.GetRelativePath(Path, absolute);

    /// <summary>
    /// Enumerates every .md file in the repo, skipping bin/obj/.git/node_modules.
    /// </summary>
    public static IEnumerable<string> MarkdownFiles()
    {
        foreach (
            var file in Directory.EnumerateFiles(Path, "*.md", SearchOption.AllDirectories)
        )
        {
            if (IsExcluded(file))
                continue;
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
