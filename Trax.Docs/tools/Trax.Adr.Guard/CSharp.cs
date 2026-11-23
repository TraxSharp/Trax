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

        while (i < source.Length)
        {
            var c = source[i];

            var commentEnd = CommentEnd(source, i);
            if (commentEnd >= 0)
            {
                if (blankComments)
                    Blank(output, i, commentEnd);
                i = commentEnd;
                continue;
            }

            if (c == '"' && i + 2 < source.Length && source[i + 1] == '"' && source[i + 2] == '"')
            {
                i = SkipRawString(source, output, i);
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
                i = SkipVerbatimString(source, output, i);
                continue;
            }

            if (c == '"' || c == '\'')
            {
                var scan = i + 1;
                while (scan < source.Length && source[scan] != c)
                    scan += source[scan] == '\\' ? 2 : 1;

                Blank(output, i + 1, Math.Min(scan, source.Length));
                i = scan < source.Length ? scan + 1 : source.Length;
                continue;
            }

            i++;
        }

        return new string(output);
    }

    /// <summary>
    /// The end of the comment or preprocessor directive starting at <paramref name="i"/>, or -1
    /// when none starts there.
    /// </summary>
    private static int CommentEnd(string source, int i)
    {
        var c = source[i];

        // Comments are walked either way. Stepping over one is what stops a quote inside
        // it from opening a literal and blanking the code that follows.
        if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
        {
            var end = source.IndexOf('\n', i);
            return end < 0 ? source.Length : end;
        }

        // A preprocessor directive runs to the end of its line and holds no literal. Its
        // text is free prose, so '#region the connection's operations' must not open a
        // character literal and blank the rest of the file.
        if (c == '#' && AtLineStart(source, i))
        {
            var end = source.IndexOf('\n', i);
            return end < 0 ? source.Length : end;
        }

        if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
        {
            var end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
            return end < 0 ? source.Length : end + 2;
        }

        return -1;
    }

    private static void Blank(char[] output, int from, int to)
    {
        for (var k = from; k < to && k < output.Length; k++)
        {
            if (output[k] != '\n' && output[k] != '\r')
                output[k] = ' ';
        }
    }

    /// <summary>
    /// Blanks the raw string literal opening at <paramref name="i"/> and returns the index
    /// after it.
    /// </summary>
    private static int SkipRawString(string source, char[] output, int i)
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

        Blank(output, i + open, Math.Min(scan, source.Length));
        return scan >= source.Length ? source.Length : scan + open;
    }

    /// <summary>
    /// Blanks the verbatim string literal opening at <paramref name="i"/> and returns the index
    /// after it.
    /// </summary>
    private static int SkipVerbatimString(string source, char[] output, int i)
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

        Blank(output, quote + 1, Math.Min(scan, source.Length));
        return scan < source.Length ? scan + 1 : source.Length;
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
