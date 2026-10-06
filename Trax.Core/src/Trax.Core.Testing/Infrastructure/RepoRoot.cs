namespace Trax.Core.Testing.Infrastructure;

/// <summary>
/// Locates the repository root by walking up from the test assembly's base directory until it finds a
/// directory containing a <c>*.slnx</c> solution file. Architecture guards scan the tree from here.
/// </summary>
public static class RepoRoot
{
    private static readonly Lazy<string> Cached = new(Resolve);

    /// <summary>The absolute path of the repository root (the directory containing a <c>*.slnx</c>).</summary>
    public static string Path => Cached.Value;

    /// <summary>Combines path segments onto the repository root.</summary>
    public static string Combine(params string[] segments) =>
        System.IO.Path.Combine([Path, .. segments]);

    /// <summary>Returns <paramref name="absolute"/> as a path relative to the repository root.</summary>
    public static string Relative(string absolute) =>
        System.IO.Path.GetRelativePath(Path, absolute);

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
