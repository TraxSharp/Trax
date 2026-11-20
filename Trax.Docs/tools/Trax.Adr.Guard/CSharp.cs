namespace Trax.Adr.Guard;

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

            // A preprocessor directive runs to the end of its line and holds no literal. Its
            // text is free prose, so '#region the connection's operations' must not open a
            // character literal and blank the rest of the file.
            if (c == '#' && AtLineStart(source, i))
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

    /// <summary>Whether only whitespace precedes <paramref name="index"/> on its line.</summary>
    private static bool AtLineStart(string source, int index)
    {
        for (var k = index - 1; k >= 0 && source[k] != '\n'; k--)
        {
            if (!char.IsWhiteSpace(source[k]))
                return false;
        }
        return true;
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
