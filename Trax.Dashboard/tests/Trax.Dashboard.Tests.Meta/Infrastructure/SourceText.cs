namespace Trax.Dashboard.Tests.Meta.Infrastructure;

internal static class SourceText
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

    public static string StripCommentsAndStrings(string source)
    {
        var s = BlockComment.Replace(source, " ");
        s = LineComment.Replace(s, " ");
        s = InterpolatedVerbatim.Replace(s, "\"\"");
        s = VerbatimString.Replace(s, "\"\"");
        s = RegularString.Replace(s, "\"\"");
        return s;
    }

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
