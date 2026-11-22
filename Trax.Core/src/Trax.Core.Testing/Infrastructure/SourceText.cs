using System.Text.RegularExpressions;

namespace Trax.Core.Testing.Infrastructure;

/// <summary>
/// Helpers for scanning C# source as text: stripping comments and string literals before a regex
/// match (so a pattern doesn't false-positive on a comment or string), and reporting matching lines.
/// </summary>
public static class SourceText
{
    /// <summary>
    /// Blanks out comment and string-literal content so a keyword scan cannot match inside one.
    /// </summary>
    /// <remarks>
    /// Comments and string literals are mutually exclusive contexts and cannot be resolved
    /// independently, which rules out a sequence of regex replacements.
    ///
    /// <para>
    /// The previous implementation stripped `//` before string literals, so a line holding a URL
    /// lost the rest of itself and the dangling quote then swallowed everything to the next quote
    /// in the file. It also had no raw-string handling, so a raw block containing quotes
    /// corrupted the scan from there on. The single left-to-right pass below closes both.
    /// </para>
    ///
    /// <para>
    /// String-literal delimiters are kept and only the content between them is blanked. Comment
    /// delimiters are blanked along with their content. Either way the output is the same length
    /// as the input and newlines are preserved, so every offset and line number in the result
    /// still points at the same place in the original.
    /// </para>
    /// </remarks>
    public static string StripCommentsAndStrings(string source)
    {
        var output = source.ToCharArray();
        var i = 0;

        while (i < source.Length)
            i = BlankAt(source, output, i);

        return new string(output);
    }

    /// <summary>
    /// Blanks the comment or literal that starts at <paramref name="i"/>, and returns where the
    /// scan goes on: past it, or to the next character when none starts there.
    /// </summary>
    private static int BlankAt(string source, char[] output, int i)
    {
        var c = source[i];

        if (c == '/' && i + 1 < source.Length && source[i + 1] == '/')
        {
            var end = source.IndexOf('\n', i);
            if (end < 0)
                end = source.Length;
            Blank(output, i, end);
            return end;
        }

        if (c == '/' && i + 1 < source.Length && source[i + 1] == '*')
        {
            var end = source.IndexOf("*/", i + 2, StringComparison.Ordinal);
            end = end < 0 ? source.Length : end + 2;
            Blank(output, i, end);
            return end;
        }

        if (BlankRawString(source, output, i) is { } afterRaw)
            return afterRaw;

        // Verbatim string, optionally interpolated: @"...", $@"...", @$"...".
        var verbatim = c == '@' && i + 1 < source.Length && source[i + 1] == '"';
        var interpolatedVerbatim =
            (c == '$' || c == '@')
            && i + 2 < source.Length
            && (
                (source[i + 1] == '@' && source[i + 2] == '"')
                || (source[i + 1] == '$' && source[i + 2] == '"')
            );

        if (verbatim || interpolatedVerbatim)
            return BlankVerbatimString(source, output, i);

        if (c == '"' || c == '\'')
        {
            var scan = i + 1;
            while (scan < source.Length && source[scan] != c)
            {
                // A backslash escapes the next character, including the delimiter.
                scan += source[scan] == '\\' ? 2 : 1;
            }

            Blank(output, i + 1, Math.Min(scan, source.Length));
            return scan < source.Length ? scan + 1 : source.Length;
        }

        return i + 1;
    }

    // Blanks source[from..to) in place, keeping line structure intact.
    private static void Blank(char[] output, int from, int to)
    {
        for (var k = from; k < to && k < output.Length; k++)
        {
            if (output[k] != '\n' && output[k] != '\r')
                output[k] = ' ';
        }
    }

    /// <summary>
    /// Blanks the raw string literal starting at <paramref name="i"/> and returns the index past
    /// it, or null when none starts there.
    /// </summary>
    private static int? BlankRawString(string source, char[] output, int i)
    {
        // Raw string literal, optionally interpolated: three or more quotes closed by the same
        // count, behind any number of leading '$'. The '$$"""' form has to be recognised here,
        // before the verbatim branch below mistakes its '$$"' prefix for '$@"'.
        var dollars = 0;
        while (i + dollars < source.Length && source[i + dollars] == '$')
            dollars++;

        if (
            !(
                i + dollars + 2 < source.Length
                && source[i + dollars] == '"'
                && source[i + dollars + 1] == '"'
                && source[i + dollars + 2] == '"'
            )
        )
            return null;

        var quoteStart = i + dollars;
        var open = 0;
        while (quoteStart + open < source.Length && source[quoteStart + open] == '"')
            open++;

        var scan = quoteStart + open;
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

        var end = scan >= source.Length ? source.Length : scan + open;
        Blank(output, quoteStart + open, Math.Min(scan, end));
        return end;
    }

    /// <summary>
    /// Blanks the verbatim string starting at <paramref name="i"/> and returns the index past it.
    /// </summary>
    private static int BlankVerbatimString(string source, char[] output, int i)
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

            // "" is an escaped quote inside a verbatim string.
            if (scan + 1 < source.Length && source[scan + 1] == '"')
            {
                scan += 2;
                continue;
            }

            break;
        }

        Blank(output, quote + 1, scan);
        return scan < source.Length ? scan + 1 : source.Length;
    }

    /// <summary>Returns the 1-based line number and text of every line in <paramref name="source"/> matching <paramref name="pattern"/>.</summary>
    public static IReadOnlyList<(int LineNumber, string Line)> MatchingLines(
        string source,
        Regex pattern
    )
    {
        var hits = new List<(int, string)>();
        var lines = source.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (pattern.IsMatch(lines[i]))
                hits.Add((i + 1, lines[i]));
        }

        return hits;
    }
}
