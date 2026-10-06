namespace Trax.Effect.Data.Testing.Tests;

/// <summary>Throwaway directory tree for exercising source-scanning guards against synthetic fixtures.</summary>
public sealed class TempRepo : IDisposable
{
    public string Root { get; } =
        Path.Combine(Path.GetTempPath(), "trax-data-guard-tests", Guid.NewGuid().ToString("N"));

    public TempRepo() => Directory.CreateDirectory(Root);

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
        catch (IOException) { }
    }
}
