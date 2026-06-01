using System.Text.RegularExpressions;

namespace Trax.Core.Testing.Infrastructure;

/// <summary>
/// Helpers for scanning C# source as text: stripping comments and string literals before a regex
/// match (so a pattern doesn't false-positive on a comment or string), and reporting matching lines.
/// </summary>
public static class SourceText
{
    private static readonly Regex BlockComment = new(
        "/\\*.*?\\*/",
        RegexOptions.Singleline | RegexOptions.Compiled
    );
    private static readonly Regex LineComment = new("//[^\\r\\n]*", RegexOptions.Compiled);
    private static readonly Regex VerbatimString = new(
        "@\"(?:[^\"]|\"\")*\"",
        RegexOptions.Compiled
    );
    private static readonly Regex InterpolatedVerbatim = new(
        "\\$@\"(?:[^\"]|\"\")*\"",
        RegexOptions.Compiled
    );
    private static readonly Regex RegularString = new(
        "\"(?:\\\\.|[^\"\\\\])*\"",
        RegexOptions.Compiled
    );

    /// <summary>
    /// Removes block/line comments and string literals (replacing strings with <c>""</c>) so a
    /// keyword scan ignores commented-out or quoted occurrences.
    /// </summary>
    public static string StripCommentsAndStrings(string source)
    {
        var s = BlockComment.Replace(source, " ");
        s = LineComment.Replace(s, " ");
        s = InterpolatedVerbatim.Replace(s, "\"\"");
        s = VerbatimString.Replace(s, "\"\"");
        s = RegularString.Replace(s, "\"\"");
        return s;
    }

    /// <summary>Returns the 1-based line number and text of every line in <paramref name="source"/> matching <paramref name="pattern"/>.</summary>
    public static IReadOnlyList<(int LineNumber, string Line)> MatchingLines(
        string source,
        Regex pattern
    )
    {
        var hits = new List<(int, string)>();
        var lines = source.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (pattern.IsMatch(lines[i]))
                hits.Add((i + 1, lines[i]));
        }

        return hits;
    }
}
