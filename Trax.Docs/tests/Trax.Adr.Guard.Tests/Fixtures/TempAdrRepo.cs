namespace Trax.Adr.Guard.Tests.Fixtures;

/// <summary>
/// Builds a throwaway repository tree on disk so the checks can be exercised against
/// synthetic corpora, with no dependency on the real ADRs. Disposing deletes the tree.
/// </summary>
/// <remarks>
/// Mirrors <c>TempRepo</c> in <c>Trax.Core.Testing.Tests</c>, which is how guard checkers
/// are already tested in this workspace. Testing a guard against the real corpus would
/// couple every test to content that changes for unrelated reasons, and would leave the
/// interesting cases (a malformed ADR, a drifted index) untestable because they must never
/// exist in the real tree.
/// </remarks>
public sealed class TempAdrRepo : IDisposable
{
    public string Root { get; } =
        Path.Combine(Path.GetTempPath(), "trax-adr-guard-tests", Guid.NewGuid().ToString("N"));

    public string AdrRoot { get; private init; } = "docs/adr";

    public TempAdrRepo() => Directory.CreateDirectory(Root);

    /// <summary>Writes a file at a repo-relative path, creating directories as needed.</summary>
    public TempAdrRepo Write(string relativePath, string content)
    {
        var full = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return this;
    }

    /// <summary>Writes an ADR into the corpus directory.</summary>
    public TempAdrRepo Adr(string fileName, string content) =>
        Write($"{AdrRoot}/{fileName}", content);

    /// <summary>Writes the corpus index.</summary>
    public TempAdrRepo Index(string content) => Write($"{AdrRoot}/README.md", content);

    /// <summary>Removes a file that a previous call wrote.</summary>
    public TempAdrRepo Delete(string relativePath)
    {
        var full = Path.Combine(Root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        if (File.Exists(full))
            File.Delete(full);
        return this;
    }

    public GuardOptions Options(
        IReadOnlySet<string>? knownAreas = null,
        IReadOnlySet<string>? knownRepos = null,
        bool requireReposKey = false,
        string? censusRoot = null,
        IReadOnlyList<string>? testRoots = null
    ) =>
        new()
        {
            RepoRoot = Root,
            AdrRoot = AdrRoot,
            TestRoots = testRoots ?? ["tests"],
            KnownAreas =
                knownAreas
                ?? new HashSet<string>(StringComparer.Ordinal) { "testing", "migrations" },
            KnownRepos =
                knownRepos ?? new HashSet<string>(StringComparer.Ordinal) { "effect", "api" },
            RequireReposKey = requireReposKey,
            CensusRoot = censusRoot,
        };

    /// <summary>
    /// The green baseline: one well-formed ADR and an index that agrees with it. Every red
    /// case perturbs exactly one thing about this, so a failure names the thing it broke.
    /// </summary>
    public static TempAdrRepo Valid(bool central = false)
    {
        var repo = new TempAdrRepo();
        var spec = Sample.DefaultSpec;
        repo.Adr(spec.FileName, Sample.Adr(central: central));
        repo.Index(Sample.Index(central, spec));
        return repo;
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
