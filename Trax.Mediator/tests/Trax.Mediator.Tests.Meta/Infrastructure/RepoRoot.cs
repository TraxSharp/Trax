namespace Trax.Mediator.Tests.Meta.Infrastructure;

internal static class RepoRoot
{
    private static readonly Lazy<string> Cached = new(Resolve);

    public static string Path => Cached.Value;

    public static string Combine(params string[] segments) =>
        System.IO.Path.Combine(new[] { Path }.Concat(segments).ToArray());

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
