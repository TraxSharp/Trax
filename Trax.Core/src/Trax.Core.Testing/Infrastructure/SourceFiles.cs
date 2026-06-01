namespace Trax.Core.Testing.Infrastructure;

/// <summary>
/// Enumerates source files under the repository root, excluding build output and dependency
/// directories. Pass subdirectories to scope the scan (e.g. <c>"src"</c>, <c>"tests"</c>); pass none
/// to scan the whole repo.
/// </summary>
public static class SourceFiles
{
    /// <summary>Enumerates <c>*.cs</c> files under the given subdirectories of the detected repo root (or the whole repo).</summary>
    public static IEnumerable<string> CSharp(params string[] subdirs) =>
        CSharpUnder(RepoRoot.Path, subdirs);

    /// <summary>Enumerates <c>*.csproj</c> files under the detected repo root.</summary>
    public static IEnumerable<string> Projects(params string[] subdirs) =>
        ProjectsUnder(RepoRoot.Path, subdirs);

    /// <summary>Enumerates <c>*.md</c> files under the detected repo root.</summary>
    public static IEnumerable<string> Markdown(params string[] subdirs) =>
        Enumerate(RepoRoot.Path, "*.md", subdirs);

    /// <summary>Enumerates <c>*.cs</c> files under an explicit root (for testing or custom roots).</summary>
    public static IEnumerable<string> CSharpUnder(string root, params string[] subdirs) =>
        Enumerate(root, "*.cs", subdirs);

    /// <summary>Enumerates <c>*.csproj</c> files under an explicit root (for testing or custom roots).</summary>
    public static IEnumerable<string> ProjectsUnder(string root, params string[] subdirs) =>
        Enumerate(root, "*.csproj", subdirs);

    private static IEnumerable<string> Enumerate(string repoRoot, string pattern, string[] subdirs)
    {
        var roots =
            subdirs.Length == 0
                ? [repoRoot]
                : subdirs.Select(s => Path.Combine(repoRoot, s)).ToArray();

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
                continue;

            foreach (
                var file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            )
            {
                if (!IsExcluded(file))
                    yield return file;
            }
        }
    }

    private static bool IsExcluded(string path)
    {
        var s = Path.DirectorySeparatorChar;
        return path.Contains($"{s}bin{s}", StringComparison.Ordinal)
            || path.Contains($"{s}obj{s}", StringComparison.Ordinal)
            || path.Contains($"{s}node_modules{s}", StringComparison.Ordinal)
            || path.Contains($"{s}.git{s}", StringComparison.Ordinal);
    }
}
