using System.Text;

namespace Trax.StateMachine.Gherkin.Lexical;

/// <summary>
/// Writes a lexicon in one layout: two spaces of indent per block, one space between words, a run of lines with the
/// same keyword lined up in columns (a state's phrase, an outcome's <c>when</c>), at most one blank line in a row, and
/// notes kept where they were. The reader ignores all of it, so formatting never changes what a lexicon elaborates to;
/// it exists for the person reading the file. Formatting twice changes nothing the second time.
/// </summary>
internal static class LexiconFormatter
{
    // The keywords whose first word after them is lined up: the second column starts at one place in a run.
    private static readonly HashSet<string> Columns = new(StringComparer.Ordinal)
    {
        "Actor:",
        "State:",
        "Trigger:",
        "Outcome:",
        "Label:",
        "Rule:",
        "Reduction:",
        "Migrates from:",
    };

    private sealed record Row(int Depth, string? Keyword, IReadOnlyList<Token> Tokens, string? Note)
    {
        internal bool Blank => Keyword is null && Note is null;
    }

    /// <summary>The lexicon in its one layout, or the problems that keep it from being read line by line.</summary>
    internal static (string? Text, IReadOnlyList<LexProblem> Problems) Format(string text)
    {
        var (lines, problems) = LexiconSyntax.Lines(text);
        if (problems.Count > 0)
            return (null, problems);
        var byNumber = lines.ToDictionary(l => l.Number);

        // Each keyword line's depth is where it attaches, by the reader's own rule.
        var open = new List<Block> { Block.Root };
        var rows = new List<Row>();
        var number = 0;
        foreach (var raw in text.Split('\n'))
        {
            number++;
            var note = Note(raw.TrimEnd('\r'));
            if (!byNumber.TryGetValue(number, out var line))
            {
                rows.Add(new Row(-1, null, [], note));
                continue;
            }
            var at = open.FindLastIndex(b => line.Keyword.Under.Contains(b));
            if (at < 0)
                at = open.Count - 1;
            open.RemoveRange(at + 1, open.Count - at - 1);
            rows.Add(new Row(at, line.Keyword.Text, line.Tokens, note));
            if (line.Keyword.Opens is { } block)
                open.Add(block);
        }

        // A note alone on its line takes the depth of the next keyword line.
        var depths = new int[rows.Count];
        var next = 0;
        for (var i = rows.Count - 1; i >= 0; i--)
        {
            if (rows[i].Keyword is not null)
                next = rows[i].Depth;
            depths[i] = rows[i].Keyword is not null ? rows[i].Depth : next;
        }

        // The widths of each run's first column.
        var widths = new int[rows.Count];
        for (var i = 0; i < rows.Count; )
        {
            var row = rows[i];
            if (row.Keyword is null || !Columns.Contains(row.Keyword) || row.Tokens.Count < 2)
            {
                i++;
                continue;
            }
            var end = i;
            while (
                end < rows.Count
                && rows[end].Keyword == row.Keyword
                && rows[end].Depth == row.Depth
                && rows[end].Tokens.Count >= 2
            )
                end++;
            var width = rows.Skip(i).Take(end - i).Max(r => Render([r.Tokens[0]]).Length);
            for (var j = i; j < end; j++)
                widths[j] = width;
            i = end;
        }

        var output = new StringBuilder();
        var blank = true;
        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            if (row.Blank)
            {
                if (!blank)
                    output.Append('\n');
                blank = true;
                continue;
            }
            blank = false;
            var indent = new string(' ', 2 * depths[i]);
            var content = row.Keyword is null ? "" : Content(row, widths[i]);
            var note = row.Note is null ? "" : (content.Length == 0 ? row.Note : "  " + row.Note);
            output.Append(indent).Append(content).Append(note).Append('\n');
        }
        // One newline at the end, never a blank line.
        var result = output.ToString().TrimEnd('\n') + "\n";
        return (result, []);
    }

    private static string Content(Row row, int width)
    {
        var text = new StringBuilder(row.Keyword);
        if (row.Tokens.Count == 0)
            return text.ToString();
        text.Append(' ');
        if (width > 0)
        {
            var first = Render([row.Tokens[0]]);
            text.Append(first)
                .Append(' ', width - first.Length + 1)
                .Append(Render(row.Tokens.Skip(1).ToList()));
        }
        else
            text.Append(Render(row.Tokens));
        return text.ToString();
    }

    // Words with one space between, a comma after the word before it, brackets around a list with no space inside.
    private static string Render(IReadOnlyList<Token> tokens)
    {
        var text = new StringBuilder();
        Token? previous = null;
        foreach (var token in tokens)
        {
            if (
                previous is { } p
                && token.Kind is not (TokenKind.Comma or TokenKind.Close)
                && p.Kind != TokenKind.Open
            )
                text.Append(' ');
            text.Append(
                token.Kind == TokenKind.Text
                    ? "\""
                        + token
                            .Value.Replace("\\", "\\\\", StringComparison.Ordinal)
                            .Replace("\"", "\\\"", StringComparison.Ordinal)
                        + "\""
                    : token.Value
            );
            previous = token;
        }
        return text.ToString();
    }

    // The note on a line, from its '#' outside quotes, or null.
    private static string? Note(string line)
    {
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (quoted && c == '\\')
            {
                i++;
                continue;
            }
            if (c == '"')
                quoted = !quoted;
            else if (c == '#' && !quoted)
                return line[i..].TrimEnd();
        }
        return null;
    }
}
