namespace Trax.Core.Testing.Tests;

/// <summary>
/// Builds a throwaway directory tree on disk so the guard checkers can be exercised against
/// synthetic fixtures via <see cref="ArchitectureGuardOptions.RepoRootOverride"/>, with no dependency
/// on the real repository contents. Disposing deletes the tree.
/// </summary>
public sealed class TempRepo : IDisposable
{
    public string Root { get; } =
        Path.Combine(Path.GetTempPath(), "trax-guard-tests", Guid.NewGuid().ToString("N"));

    public TempRepo() => Directory.CreateDirectory(Root);

    /// <summary>Writes a file at a repo-relative path, creating directories as needed.</summary>
    public TempRepo Write(string relativePath, string content)
    {
        var full = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
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
        catch (IOException)
        {
            // Best-effort cleanup; a leaked temp dir must never fail a test.
        }
    }
}
