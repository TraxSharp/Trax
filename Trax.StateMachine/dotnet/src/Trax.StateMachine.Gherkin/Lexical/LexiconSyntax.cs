using System.Globalization;
using System.Text;

namespace Trax.StateMachine.Gherkin.Lexical;

internal enum TokenKind
{
    /// <summary>A run of anything but whitespace, a quote, a comma or a bracket.</summary>
    Word,

    /// <summary>A quoted text, its escapes read.</summary>
    Text,
    Comma,
    Open,
    Close,
}

internal readonly record struct Token(TokenKind Kind, string Value)
{
    internal bool Is(string word) =>
        Kind == TokenKind.Word && string.Equals(Value, word, StringComparison.Ordinal);

    public override string ToString() =>
        Kind switch
        {
            TokenKind.Text => $"\"{Value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"",
            TokenKind.Comma => ",",
            TokenKind.Open => "[",
            TokenKind.Close => "]",
            _ => Value,
        };
}

/// <summary>The blocks a lexicon line can open, and the root every lexicon is.</summary>
internal enum Block
{
    Root,
    Actor,
    Type,
    Machine,
    State,
    Trigger,
    Guard,
    Runs,
    View,
    Field,
}

/// <summary>One keyword of the closed set: its text, its one form, where it may stand and the block it opens.</summary>
internal sealed record Keyword(string Text, string Form, Block[] Under, Block? Opens = null)
{
    /// <summary>Whether the keyword ends in a colon and takes words after it, or stands alone (<c>Differential</c>).</summary>
    internal bool TakesWords => Text.EndsWith(':');
}

/// <summary>One lexicon line: where it is, its keyword and the tokens after it.</summary>
internal sealed record LexLine(int Number, Keyword Keyword, IReadOnlyList<Token> Tokens)
{
    /// <summary>
    /// A line already reported as a problem, kept only because it opens a block: the lines under it attach to it and
    /// are not each refused for having no block to go under.
    /// </summary>
    internal bool Broken { get; init; }
}

/// <summary>A problem with a lexicon, at its line (0 for the file as a whole).</summary>
internal sealed record LexProblem(int Line, string Message);

/// <summary>
/// The lexicon's lines as Gherkin's are read: every line starts with a keyword from a closed set, whitespace outside
/// quotes never matters, <c>#</c> outside quotes starts a note, and inside quotes <c>\"</c> is a quote and <c>\\</c> a
/// backslash. Anything else is a problem at its line; nothing is skipped.
/// </summary>
internal static class LexiconSyntax
{
    private static readonly Block[] Root = [Block.Root];
    private static readonly Block[] Machine = [Block.Machine];

    /// <summary>Every keyword, in the order the grammar (<c>conformance/lexicon.md</c>) lists them.</summary>
    internal static readonly IReadOnlyList<Keyword> Keywords =
    [
        new("Version:", "Version: <number>", [Block.Root, Block.Machine]),
        new("Trains in:", "Trains in: <namespace>", Root),
        new("Actor:", "Actor: <name> \"<noun>\"", Root, Block.Actor),
        new("Roles:", "Roles: <role>, <role>", [Block.Actor]),
        new("Any signed-in", "Any signed-in", [Block.Actor]),
        new("Constant:", "Constant: <Name> is <value>", Root),
        new("Type:", "Type: <Name>", Root, Block.Type),
        new(
            "Values:",
            "Values: \"<value>\", \"<value>\"  or  Values: A to J, each followed by 1 to 10",
            [Block.Type]
        ),
        new("Has:", "Has: <field> is a <type>, <clause>", [Block.Type]),
        new("Machine:", "Machine: <id> \"<noun>\"", Root, Block.Machine),
        new("Owned by:", "Owned by: <actor>, <actor>", Machine),
        new("System", "System", Machine),
        new("System at:", "System at: <id>/<name>", Machine),
        new("System keyed by:", "System keyed by: <part>, <part>", Machine),
        new("Started by:", "Started by: \"<phrase>\"", Machine),
        new("Begins in:", "Begins in: <State>", Machine),
        new("Migrates from:", "Migrates from: <version> with <Type>", Machine),
        new("Differential", "Differential", Machine),
        new("Starts with:", "Starts with: <field> is <value>, <field> is <value>", Machine),
        new(
            "Context:",
            "Context: <field> is a <type>, <clause>  or  Context: <field> may be empty",
            [Block.Machine, Block.State]
        ),
        new(
            "State:",
            "State: <State> \"<phrase>\"  or  State: <State> \"<phrase>\" committed",
            Machine,
            Block.State
        ),
        new("Requires:", "Requires: <rule>", [Block.State]),
        new("Trigger:", "Trigger: <Trigger> \"<phrase>\"", Machine, Block.Trigger),
        new(
            "Input:",
            "Input: <field> is a <type>, <clause>  (under Trigger:)  or  Input: <field> from <context field>  (under Runs:)",
            [Block.Trigger, Block.Runs]
        ),
        new(
            "Sets:",
            "Sets: <field> to <value or field>, <field> to <value or field>",
            [Block.Trigger, Block.Runs]
        ),
        new("Keeps", "Keeps", [Block.Trigger]),
        new("Clears", "Clears", [Block.Trigger]),
        new("Resets", "Resets", [Block.Trigger]),
        new("Reduces with:", "Reduces with: <reduction>", [Block.Trigger]),
        new("Guarded by:", "Guarded by: <guard>", [Block.Trigger]),
        new(
            "Effect:",
            "Effect: <Type>, key prefix \"<prefix>\", example \"<receipt>\"",
            [Block.Trigger]
        ),
        new("Guard:", "Guard: <name> when <rule>", Machine, Block.Guard),
        new("Holds:", "Holds: \"<phrase>\"", [Block.Guard]),
        new("Fails:", "Fails: \"<phrase>\"", [Block.Guard]),
        new("Runs:", "Runs: <Train> in <State>", Machine, Block.Runs),
        new("Takes:", "Takes: <field>, <field>", [Block.Runs]),
        new("Returns:", "Returns: <field> is a <type>, <field> is a <type>", [Block.Runs]),
        new("Stores:", "Stores: <field>, <field> as <context field>", [Block.Runs]),
        new("Example:", "Example: <field> is <value>, <field> is <value>", [Block.Runs]),
        new(
            "Outcome:",
            "Outcome: \"<phrase>\" when <rule>  or  when it is done  or  when it fails  or  when it is called off",
            [Block.Runs]
        ),
        new("Link:", "Link: \"<phrase>\" to <machine> by <context field>", Machine),
        new("Rule:", "Rule: <name> with <Type>, example true", Machine),
        new("Reduction:", "Reduction: <name> with <Type>, example <field> is <value>", Machine),
        new("View:", "View: \"<name>\"", Root, Block.View),
        new("Reads:", "Reads: <machine>, <machine>", [Block.View]),
        new("Field:", "Field: <name> from <machine>'s <field or state>", [Block.View], Block.Field),
        new("Label:", "Label: <State> \"<label>\"", [Block.Field]),
    ];

    // Longest first, so "System keyed by:" is tried before "System".
    private static readonly Keyword[] ByLength = Keywords
        .OrderByDescending(k => k.Text.Length)
        .ToArray();

    /// <summary>The name a block is opened by, for a problem: <c>Runs:</c>.</summary>
    internal static string Opener(Block block) =>
        block == Block.Root ? "the top level" : Keywords.First(k => k.Opens == block).Text;

    /// <summary>Splits a lexicon into its lines, or the problems with them.</summary>
    internal static (List<LexLine> Lines, List<LexProblem> Problems) Lines(string text)
    {
        var lines = new List<LexLine>();
        var problems = new List<LexProblem>();
        var number = 0;
        foreach (var raw in text.Split('\n'))
        {
            number++;
            var line = raw.TrimEnd('\r');
            if (LoneSurrogate(line))
            {
                problems.Add(new LexProblem(number, "holds a lone surrogate, which is not text."));
                continue;
            }
            if (!Tokenize(line, out var tokens, out var problem))
            {
                problems.Add(new LexProblem(number, problem!));
                // The words before the first quote still say which block the line meant to open.
                var quote = line.IndexOf('"', StringComparison.Ordinal);
                if (
                    quote > 0
                    && Tokenize(line[..quote], out var before, out _)
                    && Match(before, out _) is { Opens: not null } opener
                )
                    lines.Add(new LexLine(number, opener, []) { Broken = true });
                continue;
            }
            if (tokens.Count == 0)
                continue;
            var keyword = Match(tokens, out var rest) ?? Match(Unglued(tokens), out rest);
            if (keyword is null)
            {
                problems.Add(new LexProblem(number, Unknown(tokens)));
                continue;
            }
            if (!keyword.TakesWords && rest.Count > 0)
            {
                problems.Add(
                    new LexProblem(
                        number,
                        $"'{keyword.Text}' stands alone on its line, with nothing after it."
                    )
                );
                continue;
            }
            if (keyword.TakesWords && rest.Count == 0)
            {
                problems.Add(new LexProblem(number, $"'{keyword.Text}' reads '{keyword.Form}'."));
                continue;
            }
            lines.Add(new LexLine(number, keyword, rest));
        }
        return (lines, problems);
    }

    // A keyword is the line's first words, compared with whitespace collapsed (which tokenizing already did) and
    // case kept: "outcome:" is not "Outcome:".
    private static Keyword? Match(List<Token> tokens, out List<Token> rest)
    {
        foreach (var keyword in ByLength)
        {
            var words = keyword.Text.Split(' ');
            if (tokens.Count < words.Length)
                continue;
            var matched = true;
            for (var i = 0; i < words.Length && matched; i++)
                matched = tokens[i].Is(words[i]);
            if (!matched)
                continue;
            rest = tokens.Skip(words.Length).ToList();
            return keyword;
        }
        rest = [];
        return null;
    }

    // "Version:1" is "Version: 1": a keyword's colon written against the word after it. Only the keyword's own words
    // are split, so a name with a colon later on the line (checkout:priced) is left alone.
    private static List<Token> Unglued(List<Token> tokens)
    {
        for (var i = 0; i < Math.Min(3, tokens.Count); i++)
        {
            var token = tokens[i];
            if (token.Kind != TokenKind.Word)
                break;
            var colon = token.Value.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0 || colon == token.Value.Length - 1)
                continue;
            return
            [
                .. tokens.Take(i),
                new Token(TokenKind.Word, token.Value[..(colon + 1)]),
                new Token(TokenKind.Word, token.Value[(colon + 1)..]),
                .. tokens.Skip(i + 1),
            ];
        }
        return tokens;
    }

    private static bool LoneSurrogate(string line)
    {
        for (var i = 0; i < line.Length; i++)
        {
            if (
                char.IsHighSurrogate(line[i])
                && i + 1 < line.Length
                && char.IsLowSurrogate(line[i + 1])
            )
                i++;
            else if (char.IsSurrogate(line[i]))
                return true;
        }
        return false;
    }

    private static string Unknown(List<Token> tokens)
    {
        // The words up to and with the first colon, or the first two, are what was meant as the keyword.
        var said = new List<string>();
        foreach (var token in tokens)
        {
            if (token.Kind != TokenKind.Word)
                break;
            said.Add(token.Value);
            if (token.Value.EndsWith(':') || said.Count == 3)
                break;
        }
        var guess = string.Join(' ', said);
        var best = Keywords
            .Select(k => (k.Text, Distance: Distance(Bare(guess), Bare(k.Text))))
            .Where(k => k.Distance <= Math.Max(2, k.Text.Length / 4))
            .OrderBy(k => k.Distance)
            .Select(k => k.Text)
            .FirstOrDefault();
        // A keyword with its words right and its colon missing (or one added) is the same keyword.
        best ??= Keywords
            .Select(k => k.Text)
            .FirstOrDefault(k =>
                guess.StartsWith(Bare(k) + " ", StringComparison.Ordinal) || guess == Bare(k)
            );
        var shown = guess.Length == 0 ? tokens[0].ToString() : guess;
        return best is null
            ? $"'{shown}' is not a lexicon keyword. Every line starts with one, and a note goes after '#'."
            : $"'{shown}' is not a lexicon keyword; did you mean '{best}'?";
    }

    private static string Bare(string keyword) => keyword.TrimEnd(':');

    private static int Distance(string a, string b)
    {
        var previous = new int[b.Length + 1];
        var current = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++)
            previous[j] = j;
        for (var i = 1; i <= a.Length; i++)
        {
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
                current[j] = Math.Min(
                    Math.Min(current[j - 1] + 1, previous[j] + 1),
                    previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1)
                );
            (previous, current) = (current, previous);
        }
        return previous[b.Length];
    }

    /// <summary>
    /// Splits one line into tokens. Outside quotes, whitespace separates, <c>,</c> <c>[</c> <c>]</c> are tokens of
    /// their own, and <c>#</c> ends the line.
    /// </summary>
    internal static bool Tokenize(string line, out List<Token> tokens, out string? problem)
    {
        var list = new List<Token>();
        tokens = list;
        problem = null;
        var word = new StringBuilder();
        void EndWord()
        {
            if (word.Length == 0)
                return;
            list.Add(new Token(TokenKind.Word, word.ToString()));
            word.Clear();
        }
        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (c == '#')
                break;
            if (c == '"')
            {
                EndWord();
                var text = new StringBuilder();
                var closed = false;
                for (i++; i < line.Length; i++)
                {
                    var q = line[i];
                    if (q == '\\')
                    {
                        if (i + 1 < line.Length && line[i + 1] is '"' or '\\')
                        {
                            text.Append(line[++i]);
                            continue;
                        }
                        problem =
                            "a backslash in quotes escapes only a quote (\\\") or a backslash (\\\\).";
                        return false;
                    }
                    if (q == '"')
                    {
                        closed = true;
                        break;
                    }
                    text.Append(q);
                }
                if (!closed)
                {
                    problem = "a quote is opened and never closed.";
                    return false;
                }
                tokens.Add(new Token(TokenKind.Text, text.ToString()));
                continue;
            }
            if (char.IsWhiteSpace(c))
            {
                EndWord();
                continue;
            }
            if (c is ',' or '[' or ']')
            {
                EndWord();
                tokens.Add(
                    new Token(
                        c == ',' ? TokenKind.Comma
                            : c == '[' ? TokenKind.Open
                            : TokenKind.Close,
                        c.ToString()
                    )
                );
                continue;
            }
            word.Append(c);
        }
        EndWord();
        return true;
    }

    /// <summary>A word as a number, read as JSON reads one.</summary>
    internal static bool Number(Token token, out double number)
    {
        number = 0;
        return token.Kind == TokenKind.Word
            && (token.Value[0] is '-' or (>= '0' and <= '9'))
            && double.TryParse(
                token.Value,
                NumberStyles.AllowLeadingSign
                    | NumberStyles.AllowDecimalPoint
                    | NumberStyles.AllowExponent,
                CultureInfo.InvariantCulture,
                out number
            )
            && double.IsFinite(number);
    }
}
