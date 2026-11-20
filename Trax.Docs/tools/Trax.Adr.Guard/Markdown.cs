namespace Trax.Adr.Guard;

/// <summary>
/// The markdown reading an ADR check needs. One implementation, because the index reader
/// and the ADR reader ask the same questions and a second copy is a second thing to get
/// wrong.
/// </summary>
public static partial class Markdown
{
    /// <summary>
    /// The body of a <c>## Heading</c> section, or null when there is no such heading.
    ///
    /// <para>
    /// Headings inside fenced code blocks are ignored, in both directions. An ADR that
    /// quotes the format template (the most likely thing for an ADR about ADRs to do)
    /// would otherwise have its fenced <c>## Exemplars</c> read as the real one, and a
    /// section whose only content was an example would satisfy a check it never met.
    /// </para>
    /// </summary>
    public static string? Section(string text, string heading)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var code = CodeLines(lines);
        var wanted = $"## {heading}";
        var start = -1;

        for (var i = 0; i < lines.Length; i++)
        {
            if (!code[i] && string.Equals(lines[i].TrimEnd(), wanted, StringComparison.Ordinal))
            {
                start = i + 1;
                break;
            }
        }

        if (start < 0)
            return null;

        var body = new List<string>();
        for (var i = start; i < lines.Length; i++)
        {
            if (!code[i] && lines[i].StartsWith("## ", StringComparison.Ordinal))
                break;
            body.Add(lines[i]);
        }

        return string.Join('\n', body).Trim();
    }

    /// <summary>
    /// The same text with every code block removed, fenced or indented, for the checks that
    /// read prose and must not mistake an example for the real thing.
    /// </summary>
    public static string WithoutFences(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        var code = CodeLines(lines);
        var kept = new List<string>();

        for (var i = 0; i < lines.Length; i++)
        {
            if (!code[i])
                kept.Add(lines[i]);
        }

        return string.Join('\n', kept);
    }

    /// <summary>
    /// Data rows of the markdown tables in a section, as cell arrays. The header row and
    /// its <c>---</c> separator are skipped: a row counts as data only once a separator
    /// has been seen, which is what distinguishes the two in markdown.
    /// </summary>
    public static IEnumerable<string[]> TableRows(string section, int minimumCells)
    {
        var seenSeparator = false;

        foreach (var raw in Markdown.WithoutFences(section).Split('\n'))
        {
            var line = raw.Trim();

            if (!line.StartsWith('|'))
            {
                seenSeparator = false; // a blank line or prose ends the table
                continue;
            }

            var cells = SplitCells(line);

            if (cells.All(c => c.Trim().Trim('-', ':').Length == 0))
            {
                seenSeparator = true;
                continue;
            }

            if (!seenSeparator || cells.Length < minimumCells)
                continue;

            yield return cells;
        }
    }

    /// <summary>Splits a table row on unescaped pipes, so a cell may contain <c>\|</c>.</summary>
    private static string[] SplitCells(string line) =>
        UnescapedPipe
            .Split(line.Trim('|'))
            .Select(c => c.Replace("\\|", "|", StringComparison.Ordinal))
            .ToArray();

    [GeneratedRegex(@"(?<!\\)\|", RegexOptions.None, matchTimeoutMilliseconds: 1000)]
    private static partial Regex UnescapedPipe { get; }

    /// <summary>A fence line: the character it is drawn with, how long its run is, and what follows.</summary>
    private readonly record struct Fence(char Character, int Length, bool Bare);

    /// <summary>
    /// The fence a line opens or closes, or null if it is not a fence line.
    ///
    /// <para>
    /// A fence closes only on the character that opened it, with a run at least as long and
    /// nothing after it, which is CommonMark's rule. Treating ``` and ~~~ as interchangeable
    /// meant a tilde line shown inside a backtick block left the fence open. Treating any run
    /// of three as the same marker meant a four-backtick block quoting a three-backtick one
    /// closed on the inner opener, and a heading the outer block quoted was read as the
    /// document's own.
    /// </para>
    /// </summary>
    private static Fence? FenceOf(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.Length < 3 || (trimmed[0] != '`' && trimmed[0] != '~'))
            return null;

        var character = trimmed[0];
        var length = 0;
        while (length < trimmed.Length && trimmed[length] == character)
            length++;

        return length < 3
            ? null
            : new Fence(character, length, trimmed[length..].Trim().Length == 0);
    }

    /// <summary>Whether a fence line closes the block <paramref name="open"/> started.</summary>
    private static bool Closes(Fence line, Fence open) =>
        line.Character == open.Character && line.Length >= open.Length && line.Bare;

    /// <summary>
    /// Lines that markdown renders as code: fenced blocks, and blocks indented by four spaces
    /// or a tab after a blank line. An exemplar claim shown as an indented example is an
    /// example, not a claim, the same as a fenced one.
    /// </summary>
    private static bool[] CodeLines(string[] lines)
    {
        var code = new bool[lines.Length];
        Fence? fence = null;
        var previousBlank = true;

        for (var i = 0; i < lines.Length; i++)
        {
            var marker = FenceOf(lines[i]);

            if (fence is { } open)
            {
                code[i] = true;
                if (marker is { } line && Closes(line, open))
                    fence = null;
                previousBlank = false;
                continue;
            }

            if (marker is not null)
            {
                fence = marker;
                code[i] = true;
                previousBlank = false;
                continue;
            }

            var blank = lines[i].Trim().Length == 0;
            var indented =
                !blank
                && (
                    lines[i].StartsWith("    ", StringComparison.Ordinal)
                    || lines[i].StartsWith('\t')
                );

            // An indented block only opens after a blank line; otherwise it is a wrapped list
            // item or a continuation, which ADRs use constantly.
            if (indented && previousBlank)
            {
                while (
                    i < lines.Length
                    && (
                        lines[i].Trim().Length == 0
                        || lines[i].StartsWith("    ", StringComparison.Ordinal)
                        || lines[i].StartsWith('\t')
                    )
                )
                {
                    code[i] = lines[i].Trim().Length != 0;
                    i++;
                }
                i--;
                previousBlank = false;
                continue;
            }

            previousBlank = blank;
        }

        return code;
    }
}
