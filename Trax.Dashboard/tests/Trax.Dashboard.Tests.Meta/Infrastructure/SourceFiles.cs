namespace Trax.Dashboard.Tests.Meta.Infrastructure;

internal static class SourceFiles
{
    public static IEnumerable<string> CSharp(params string[] subdirs) => Enumerate("*.cs", subdirs);

    public static IEnumerable<string> Projects(params string[] subdirs) =>
        Enumerate("*.csproj", subdirs);

    public static IEnumerable<string> Markdown(params string[] subdirs) =>
        Enumerate("*.md", subdirs);

    private static IEnumerable<string> Enumerate(string pattern, string[] subdirs)
    {
        var roots =
            subdirs.Length == 0
                ? new[] { RepoRoot.Path }
                : subdirs.Select(s => Path.Combine(RepoRoot.Path, s)).ToArray();

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
                continue;
            foreach (
                var file in Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories)
            )
            {
                if (IsExcluded(file))
                    continue;
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
