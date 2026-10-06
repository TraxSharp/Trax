namespace Trax.Effect.Tests.Meta.Infrastructure;

/// <summary>
/// A throwaway directory tree, for showing that a guard fails on a violation it is meant to catch.
/// </summary>
internal sealed class SyntheticRepo : IDisposable
{
    public string Root { get; } =
        System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "trax-effect-meta-tests",
            Guid.NewGuid().ToString("N")
        );

    public SyntheticRepo() => Directory.CreateDirectory(Root);

    public SyntheticRepo Write(string relativePath, string content)
    {
        var full = System.IO.Path.Combine(
            Root,
            relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar)
        );
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return this;
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, recursive: true);
        }
        catch (IOException) { }
    }
}
