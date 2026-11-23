namespace Trax.Adr.Guard;

/// <summary>
/// Finds the ADRs a run is checking, and the index that must agree with them.
/// </summary>
/// <remarks>
/// Discovery is the part that fails silently when it drifts: a renamed directory turns
/// every "no violations" result vacuous at once. Every check therefore reports
/// <see cref="GuardResult.Inspected"/>, and the runner treats a zero as a failure rather
/// than a pass.
/// </remarks>
public static partial class AdrCorpus
{
    public const string IndexFileName = "README.md";

    /// <summary>A well-formed ADR file name: four digits, a hyphen, then a kebab-case slug.</summary>
    [GeneratedRegex(
        @"^(?<number>\d{4})-(?<slug>[a-z0-9]+(?:-[a-z0-9]+)*)\.md$",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000
    )]
    public static partial Regex FileNamePattern { get; }

    public static string AdrDirectory(GuardOptions options) =>
        Path.Combine(options.RepoRoot, options.AdrRoot.Replace('/', Path.DirectorySeparatorChar));

    /// <summary>
    /// The corpus index. Matched case-insensitively so a <c>readme.md</c> behaves the same on a
    /// case-insensitive filesystem as on Linux CI, rather than passing locally and failing there.
    /// </summary>
    public static string IndexPath(GuardOptions options)
    {
        var dir = AdrDirectory(options);
        if (!Directory.Exists(dir))
            return Path.Combine(dir, IndexFileName);

        var found = Directory
            .EnumerateFiles(dir, "*.md", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(f =>
                Path.GetFileName(f).Equals(IndexFileName, StringComparison.OrdinalIgnoreCase)
            );

        return found ?? Path.Combine(dir, IndexFileName);
    }

    /// <summary>
    /// Every ADR in the configured directory, in file-name order. The index is not an ADR
    /// and is excluded. Files that do not match the naming pattern are still returned, so
    /// the naming check can report them rather than discovery hiding them.
    /// </summary>
    public static async Task<IReadOnlyList<Adr>> DiscoverAsync(GuardOptions options)
    {
        var dir = AdrDirectory(options);
        if (!Directory.Exists(dir))
            return [];

        // Recursive on purpose. Scanning only the top directory meant a file moved into an
        // archive/ subfolder silently left enforcement while still looking like an ADR.
        var files = Directory
            .EnumerateFiles(dir, "*.md", SearchOption.AllDirectories)
            .Where(f =>
                !Path.GetFileName(f).Equals(IndexFileName, StringComparison.OrdinalIgnoreCase)
            )
            .OrderBy(Path.GetFileName, StringComparer.Ordinal);

        var adrs = new List<Adr>();
        foreach (var file in files)
            adrs.Add(await LoadAsync(file, options).ConfigureAwait(false));
        return adrs;
    }

    private static async Task<Adr> LoadAsync(string absolutePath, GuardOptions options)
    {
        var text = (await File.ReadAllTextAsync(absolutePath).ConfigureAwait(false)).Replace(
            "\r\n",
            "\n",
            StringComparison.Ordinal
        );
        var fileName = Path.GetFileName(absolutePath);
        var match = FileNamePattern.Match(fileName);

        return new Adr
        {
            AbsolutePath = absolutePath,
            RelativePath = Relative(absolutePath, options),
            FileName = fileName,
            Number = match.Success ? match.Groups["number"].Value : fileName,
            Text = text,
            Frontmatter = Frontmatter.Parse(text),
        };
    }

    public static string Relative(string absolutePath, GuardOptions options) =>
        Path.GetRelativePath(options.RepoRoot, absolutePath)
            .Replace(Path.DirectorySeparatorChar, '/');
}
