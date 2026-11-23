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
