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
        var lines = text.Replace("\r\n", "\n").Split('\n');
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
        Regex.Split(line.Trim('|'), @"(?<!\\)\|").Select(c => c.Replace("\\|", "|")).ToArray();

    /// <summary>
    /// The fence marker a line opens or closes, or null if it is not a fence line.
    ///
    /// <para>
    /// A fence closes only on the marker that opened it. Treating ``` and ~~~ as
    /// interchangeable meant a tilde line shown inside a backtick block left the fence open,
    /// and every section lookup after it failed on a document that was perfectly well formed.
    /// </para>
    /// </summary>
    private static string? FenceMarker(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
            return "```";
        return trimmed.StartsWith("~~~", StringComparison.Ordinal) ? "~~~" : null;
    }

    /// <summary>
    /// Lines that markdown renders as code: fenced blocks, and blocks indented by four spaces
    /// or a tab after a blank line. An exemplar claim shown as an indented example is an
    /// example, not a claim, the same as a fenced one.
    /// </summary>
    private static bool[] CodeLines(string[] lines)
    {
        var code = new bool[lines.Length];
        string? fence = null;
        var previousBlank = true;

        for (var i = 0; i < lines.Length; i++)
        {
            var marker = FenceMarker(lines[i]);

            if (fence is not null)
            {
                code[i] = true;
                if (marker == fence)
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
                    || lines[i].StartsWith("\t", StringComparison.Ordinal)
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
                        || lines[i].StartsWith("\t", StringComparison.Ordinal)
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

/// <summary>
/// Reading C# source well enough to tell a declaration from a mention of one.
/// </summary>
public static class CSharp
{
    /// <summary>
    /// The source with comment bodies and string literals blanked out, so a class named in a
    /// comment or held as fixture text is not read as a declaration.
    /// </summary>
    /// <remarks>
    /// A single left-to-right pass. The previous line-based version missed block comments
    /// entirely, so an ADR could claim a guard that existed only inside a <c>/* */</c>, and it
    /// toggled on any line containing a triple quote, so a single-line raw string turned the
    /// scanner off for the rest of the file and hid every later class.
    ///
    /// <para>Output is the same length as the input, newlines preserved, so line numbers hold.</para>
    /// </remarks>
    public static string WithoutCommentsAndLiterals(string source) =>
        Blanked(source, blankComments: true);

    /// <summary>
    /// The source with string literals blanked and comments left as they are, for the scans
    /// that have to read a documentation comment.
    /// </summary>
    /// <remarks>
    /// The same pass as <see cref="WithoutCommentsAndLiterals"/>, so the two agree about what
    /// a literal is and the output is the same length with newlines preserved: a line number
    /// taken from one indexes the other unchanged. Reading a docstring from the raw text
    /// instead let a <c>///</c> line quoted inside a literal sit above a class and answer for
    /// it.
    /// </remarks>
    public static string WithoutLiterals(string source) => Blanked(source, blankComments: false);

    private static string Blanked(string source, bool blankComments)
    {
        var output = source.ToCharArray();
        var i = 0;

        void Blank(int from, int to)
        {
            for (var k = from; k < to && k < output.Length; k++)
            {
                if (output[k] != '\n' && output[k] != '\r')
                    output[k] = ' ';
            }
        }

        while (i < source.Length)
        {
            var c = source[i];

            // Comments are walked either way. Stepping over one is what stops a quote inside
            // it from opening a literal and blanking the code that follows.
            if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
            {
                var end = source.IndexOf('\n', i);
                end = end < 0 ? source.Length : end;
                if (blankComments)
                    Blank(i, end);
                i = end;
                continue;
            }

            if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
            {
                var end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
                end = end < 0 ? source.Length : end + 2;
                if (blankComments)
                    Blank(i, end);
                i = end;
                continue;
            }

            if (c == '"' && i + 2 < source.Length && source[i + 1] == '"' && source[i + 2] == '"')
            {
                var open = 0;
                while (i + open < source.Length && source[i + open] == '"')
                    open++;

                var scan = i + open;
                while (scan < source.Length)
                {
                    if (source[scan] != '"')
                    {
                        scan++;
                        continue;
                    }

                    var run = 0;
                    while (scan + run < source.Length && source[scan + run] == '"')
                        run++;
                    if (run >= open)
                        break;
                    scan += run;
                }

                Blank(i + open, Math.Min(scan, source.Length));
                i = scan >= source.Length ? source.Length : scan + open;
                continue;
            }

            var verbatim = c == '@' && i + 1 < source.Length && source[i + 1] == '"';
            var interpolated =
                (c == '$' || c == '@')
                && i + 2 < source.Length
                && (
                    (source[i + 1] == '@' && source[i + 2] == '"')
                    || (source[i + 1] == '$' && source[i + 2] == '"')
                );

            if (verbatim || interpolated)
            {
                var quote = source.IndexOf('"', i);
                var scan = quote + 1;
                while (scan < source.Length)
                {
                    if (source[scan] != '"')
                    {
                        scan++;
                        continue;
                    }

                    if (scan + 1 < source.Length && source[scan + 1] == '"')
                    {
                        scan += 2;
                        continue;
                    }

                    break;
                }

                Blank(quote + 1, Math.Min(scan, source.Length));
                i = scan < source.Length ? scan + 1 : source.Length;
                continue;
            }

            if (c == '"' || c == '\'')
            {
                var scan = i + 1;
                while (scan < source.Length && source[scan] != c)
                    scan += source[scan] == '\\' ? 2 : 1;

                Blank(i + 1, Math.Min(scan, source.Length));
                i = scan < source.Length ? scan + 1 : source.Length;
                continue;
            }

            i++;
        }

        return new string(output);
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
