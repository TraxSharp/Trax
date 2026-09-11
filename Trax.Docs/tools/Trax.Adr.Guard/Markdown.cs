namespace Trax.Adr.Guard;

/// <summary>
/// The markdown reading an ADR check needs. One implementation, because the index reader
/// and the ADR reader ask the same questions and a second copy is a second thing to get
/// wrong.
/// </summary>
public static class Markdown
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
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var wanted = $"## {heading}";
        var inFence = false;
        var start = -1;

        for (var i = 0; i < lines.Length; i++)
        {
            if (IsFence(lines[i]))
            {
                inFence = !inFence;
                continue;
            }

            if (!inFence && string.Equals(lines[i].TrimEnd(), wanted, StringComparison.Ordinal))
            {
                start = i + 1;
                break;
            }
        }

        if (start < 0)
            return null;

        var body = new List<string>();
        inFence = false;

        for (var i = start; i < lines.Length; i++)
        {
            if (IsFence(lines[i]))
            {
                inFence = !inFence;
                body.Add(lines[i]);
                continue;
            }

            if (!inFence && lines[i].StartsWith("## ", StringComparison.Ordinal))
                break;

            body.Add(lines[i]);
        }

        return string.Join('\n', body).Trim();
    }

    /// <summary>
    /// The same text with every fenced block removed, for the checks that read prose and
    /// must not mistake an example for the real thing.
    /// </summary>
    public static string WithoutFences(string text)
    {
        var kept = new List<string>();
        var inFence = false;

        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            if (IsFence(line))
            {
                inFence = !inFence;
                continue;
            }

            if (!inFence)
                kept.Add(line);
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
        Regex.Split(line.Trim('|'), @"(?<!\\)\|").Select(c => c.Replace("\\|", "|")).ToArray();

    private static bool IsFence(string line)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith("```", StringComparison.Ordinal)
            || trimmed.StartsWith("~~~", StringComparison.Ordinal);
    }
}

/// <summary>
/// Reading C# source well enough to tell a declaration from a mention of one.
/// </summary>
public static class CSharp
{
    /// <summary>
    /// The source with comment bodies and raw string literals blanked out, so a class
    /// named in a comment or held as fixture text is not read as a declaration.
    /// </summary>
    /// <remarks>
    /// Not a parser. It is deliberately crude, and it errs toward blanking: the cost of
    /// missing a real declaration is an ADR claim that fails to resolve and gets looked at,
    /// while the cost of keeping a commented one is an ADR claiming a guard that does not
    /// exist. Only the second failure is silent.
    /// </remarks>
    public static string WithoutCommentsAndLiterals(string source)
    {
        var kept = new List<string>();
        var inRawString = false;

        foreach (var line in source.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.Contains("\"\"\"", StringComparison.Ordinal))
            {
                inRawString = !inRawString;
                kept.Add(string.Empty);
                continue;
            }

            if (inRawString)
            {
                kept.Add(string.Empty);
                continue;
            }

            var trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith('*'))
            {
                kept.Add(string.Empty);
                continue;
            }

            var comment = line.IndexOf("//", StringComparison.Ordinal);
            kept.Add(comment >= 0 ? line[..comment] : line);
        }

        return string.Join('\n', kept);
    }

    /// <summary>Which kind of line a citation was found on.</summary>
    public enum LineKind
    {
        /// <summary>A <c>///</c> documentation comment.</summary>
        Documentation,

        /// <summary>An ordinary <c>//</c> comment, which carries no weight as a citation.</summary>
        Comment,

        /// <summary>Anything else, which in practice is an assertion message or a constant.</summary>
        Code,
    }

    public static LineKind Classify(string line)
    {
        var trimmed = line.TrimStart();

        if (trimmed.StartsWith("///", StringComparison.Ordinal))
            return LineKind.Documentation;

        return trimmed.StartsWith("//", StringComparison.Ordinal)
            ? LineKind.Comment
            : LineKind.Code;
    }
}
