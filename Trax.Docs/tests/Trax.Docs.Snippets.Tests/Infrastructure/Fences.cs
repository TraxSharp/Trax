namespace Trax.Docs.Snippets.Tests.Infrastructure;

/// <summary>
/// A fenced code block in a markdown page, with where it sits and what its info string asks for.
/// </summary>
/// <param name="Page">The page, relative to the repo root, with forward slashes.</param>
/// <param name="OpenLine">The 1-based line of the opening fence.</param>
/// <param name="Language">The first word of the info string, empty when there is none.</param>
/// <param name="Groups">
/// The compilations this fence joins. Empty when the fence is not marked. A bare
/// <c>compile</c> puts the fence in a compilation of its own.
/// </param>
/// <param name="Code">The fence's content, with the fence's own indentation removed.</param>
public sealed record Fence(
    string Page,
    int OpenLine,
    string Language,
    IReadOnlyList<string> Groups,
    string Code
)
{
    /// <summary>The 1-based line of the fence's first line of code.</summary>
    public int FirstCodeLine => OpenLine + 1;

    public bool IsMarked => Groups.Count > 0;

    public bool IsCSharp => Language is "csharp" or "cs" or "c#";

    public string Location => $"{Page}:{OpenLine}";
}

/// <summary>
/// Finds the fenced code blocks in a markdown page.
///
/// <para>
/// A fence is compiled when its info string carries the word <c>compile</c> after the language:
/// <c>```csharp compile</c> compiles that fence on its own, and <c>```csharp compile=app</c> (or
/// <c>compile=app,api</c>) compiles it together with every other fence on the same page that names
/// the same group, the way the files of one project compile together. The website ignores info
/// string words it does not know, so the marker does not show on the rendered page.
/// </para>
/// </summary>
public static class Fences
{
    public const string Marker = "compile";

    private static readonly Regex Opening = new(
        @"^(?<indent>[ \t]*)(?<fence>`{3,}|~{3,})(?<info>.*)$",
        RegexOptions.Compiled
    );

    public static IEnumerable<Fence> In(string page, string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');

        for (var i = 0; i < lines.Length; i++)
        {
            var open = Opening.Match(lines[i]);
            if (!open.Success)
                continue;

            var fence = open.Groups["fence"].Value;
            var info = open.Groups["info"].Value.Trim();

            // A backtick fence cannot carry a backtick in its info string; that line is inline
            // code, not a fence.
            if (fence[0] == '`' && info.Contains('`'))
                continue;

            var indent = open.Groups["indent"].Value.Length;
            var closing = new Regex(
                $@"^[ \t]*{Regex.Escape(fence[0].ToString())}{{{fence.Length},}}[ \t]*$"
            );

            var body = new List<string>();
            var j = i + 1;
            while (j < lines.Length && !closing.IsMatch(lines[j]))
            {
                body.Add(Unindent(lines[j], indent));
                j++;
            }

            var words = info.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            var language = words.Length > 0 ? words[0].ToLowerInvariant() : "";
            var groups = GroupsOf(words.Skip(1), i + 1);

            yield return new Fence(page, i + 1, language, groups, string.Join("\n", body));
            i = j;
        }
    }

    /// <summary>Every fence in every page of the docs.</summary>
    public static IEnumerable<Fence> InDocs() =>
        RepoRoot
            .MarkdownFiles()
            .SelectMany(file =>
                In(RepoRoot.Relative(file).Replace('\\', '/'), File.ReadAllText(file))
            );

    private static List<string> GroupsOf(IEnumerable<string> words, int openLine)
    {
        var groups = new List<string>();
        foreach (var word in words)
        {
            if (word == Marker)
                groups.Add($"line {openLine}");
            else if (word.StartsWith(Marker + "=", StringComparison.Ordinal))
                groups.AddRange(
                    word[(Marker.Length + 1)..]
                        .Split(
                            ',',
                            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                        )
                );
        }
        return groups;
    }

    private static string Unindent(string line, int indent)
    {
        var strip = 0;
        while (strip < indent && strip < line.Length && (line[strip] == ' ' || line[strip] == '\t'))
            strip++;
        return line[strip..];
    }
}
