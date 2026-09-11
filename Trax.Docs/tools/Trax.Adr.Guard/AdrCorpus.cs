namespace Trax.Adr.Guard;

/// <summary>One ADR on disk, with the two views every check needs: named sections and frontmatter.</summary>
public sealed class Adr
{
    private static readonly string[] LineBreak = ["\n"];

    public required string AbsolutePath { get; init; }

    /// <summary>Path relative to the repo root, forward-slashed, for offender messages.</summary>
    public required string RelativePath { get; init; }

    /// <summary>The file name, which is what the index and a supersession link cite.</summary>
    public required string FileName { get; init; }

    /// <summary>The four-digit number an ADR is cited by, from its file name.</summary>
    public required string Number { get; init; }

    public required string Text { get; init; }

    public required Frontmatter Frontmatter { get; init; }

    /// <summary>The <c>#</c> heading, which the index entry must match. Null when absent.</summary>
    public string? Title
    {
        get
        {
            foreach (var line in Text.Split(LineBreak, StringSplitOptions.None))
            {
                if (line.StartsWith("# ", StringComparison.Ordinal))
                    return line[2..].Trim();
            }
            return null;
        }
    }

    /// <summary>
    /// The body of a <c>## Heading</c> section, or null when the document has no such
    /// heading. Fence-aware: see <see cref="Markdown.Section"/>.
    /// </summary>
    public string? Section(string heading) => Markdown.Section(Text, heading);
}

/// <summary>
/// Finds the ADRs a run is checking, and the index that must agree with them.
/// </summary>
/// <remarks>
/// Discovery is the part that fails silently when it drifts: a renamed directory turns
/// every "no violations" result vacuous at once. Every check therefore reports
/// <see cref="GuardResult.Inspected"/>, and the runner treats a zero as a failure rather
/// than a pass.
/// </remarks>
public static class AdrCorpus
{
    public const string IndexFileName = "README.md";

    /// <summary>A well-formed ADR file name: four digits, a hyphen, then a kebab-case slug.</summary>
    public static readonly Regex FileNamePattern = new(
        @"^(?<number>\d{4})-(?<slug>[a-z0-9]+(?:-[a-z0-9]+)*)\.md$",
        RegexOptions.Compiled
    );

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
    public static IReadOnlyList<Adr> Discover(GuardOptions options)
    {
        var dir = AdrDirectory(options);
        if (!Directory.Exists(dir))
            return [];

        // Recursive on purpose. Scanning only the top directory meant a file moved into an
        // archive/ subfolder silently left enforcement while still looking like an ADR.
        return Directory
            .EnumerateFiles(dir, "*.md", SearchOption.AllDirectories)
            .Where(f =>
                !Path.GetFileName(f).Equals(IndexFileName, StringComparison.OrdinalIgnoreCase)
            )
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .Select(f => Load(f, options))
            .ToList();
    }

    private static Adr Load(string absolutePath, GuardOptions options)
    {
        var text = File.ReadAllText(absolutePath).Replace("\r\n", "\n");
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
