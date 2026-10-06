namespace Trax.Cli.Tests.Meta.Infrastructure;

/// <summary>
/// The root of the repository this folder lives in, which holds the CI configuration
/// (<c>.github/</c>) shared by every folder. <see cref="RepoRoot"/> is the folder
/// itself; this is the first directory above it with a <c>.git</c> entry (a directory in a clone,
/// a file in a worktree).
/// </summary>
internal static class MonorepoRoot
{
    private static readonly Lazy<string> Cached = new(Resolve);

    public static string Path => Cached.Value;

    public static string Combine(params string[] segments) =>
        System.IO.Path.Combine(new[] { Path }.Concat(segments).ToArray());

    private static string Resolve()
    {
        var dir = new DirectoryInfo(RepoRoot.Path);
        while (dir is not null)
        {
            var git = System.IO.Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(git) || File.Exists(git))
                return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            $"Could not locate the repository root: no .git found walking up from '{RepoRoot.Path}'."
        );
    }
}
