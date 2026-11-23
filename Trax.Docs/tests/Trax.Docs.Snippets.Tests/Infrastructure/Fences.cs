using System.Globalization;

namespace Trax.Docs.Snippets.Tests.Infrastructure;

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
/// <para>
/// Two other markers say why a fence is not compiled. <c>fragment</c> declares it partial on
/// purpose (a signature, a before and after, a line out of context), and
/// <c>from=Trax.Samples/samples/X/Program.cs#docs-setup</c> declares it an excerpt of a real
/// file, which <c>SampleExcerptsMatchSourceTests</c> holds it to.
/// </para>
/// <para>
/// A compiled fence may also name the file it shows, <c>```csharp compile=app file=Program.cs</c>,
/// which lets a later page replace it (see <see cref="SnippetUnit"/>).
/// </para>
/// </summary>
public static partial class Fences
{
    public const string Marker = "compile";

    public const string FragmentMarker = "fragment";

    public const string FromMarker = "from=";

    /// <summary>
    /// Names the file a compiled fence shows. It is not a marker: it says which file, not how the
    /// fence relates to the code, so it goes beside <c>compile</c> rather than instead of it.
    /// </summary>
    public const string FileWord = "file=";

    [GeneratedRegex(
        @"^(?<indent>[ \t]*)(?<fence>`{3,}|~{3,})(?<info>.*)$",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex Opening { get; }

    [GeneratedRegex(
        @"^ {0,3}#{1,2}[ \t]+(?<text>.*?)(?:[ \t]+#+)?[ \t]*$",
        RegexOptions.None,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex Heading { get; }

    public static IEnumerable<Fence> In(string page, string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        string? section = null;

        for (var i = 0; i < lines.Length; i++)
        {
            var open = Opening.Match(lines[i]);
            if (!open.Success)
            {
                var heading = Heading.Match(lines[i]);
                if (heading.Success)
                    section = heading.Groups["text"].Value;
                continue;
            }

            var fence = open.Groups["fence"].Value;
            var info = open.Groups["info"].Value.Trim();

            // A backtick fence cannot carry a backtick in its info string; that line is inline
            // code, not a fence.
            if (fence[0] == '`' && info.Contains('`', StringComparison.Ordinal))
                continue;

            var indent = open.Groups["indent"].Value.Length;
            var closing = new Regex(
                $@"^[ \t]*{Regex.Escape(fence[0].ToString())}{{{fence.Length},}}[ \t]*$",
                RegexOptions.None,
                TimeSpan.FromSeconds(1)
            );

            var body = new List<string>();
            var j = i + 1;
            while (j < lines.Length && !closing.IsMatch(lines[j]))
            {
                body.Add(Unindent(lines[j], indent));
                j++;
            }

            yield return Described(page, i + 1, info, body, section);
            i = j;
        }
    }

    /// <summary>The fence opening on <paramref name="line"/>, described by its info string.</summary>
    private static Fence Described(
        string page,
        int line,
        string info,
        List<string> body,
        string? section
    )
    {
        var words = info.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var language = words.Length > 0 ? words[0].ToLowerInvariant() : "";
        var markers = words.Skip(1).ToList();
        var from = markers
            .FirstOrDefault(w => w.StartsWith(FromMarker, StringComparison.Ordinal))
            ?[FromMarker.Length..];
        var hash = from?.IndexOf('#', StringComparison.Ordinal) ?? -1;

        return new Fence(page, line, language, GroupsOf(markers, line), string.Join('\n', body))
        {
            IsFragment = markers.Contains(FragmentMarker, StringComparer.Ordinal),
            From = from is null || hash < 0 ? from : from[..hash],
            FromRegion = from is null || hash < 0 ? null : from[(hash + 1)..],
            File = markers
                .FirstOrDefault(w => w.StartsWith(FileWord, StringComparison.Ordinal))
                ?[FileWord.Length..],
            Section = section,
        };
    }

    /// <summary>Every fence in every markdown file under the docs root, published or not.</summary>
    public static async Task<IReadOnlyList<Fence>> InDocsAsync()
    {
        var fences = new List<Fence>();
        foreach (var file in RepoRoot.MarkdownFiles())
            fences.AddRange(
                In(
                    RepoRoot.Relative(file).Replace('\\', '/'),
                    await File.ReadAllTextAsync(file).ConfigureAwait(false)
                )
            );
        return fences;
    }

    /// <summary>Every fence on a page traxsharp.net publishes.</summary>
    public static async Task<IReadOnlyList<Fence>> OnPublishedPagesAsync() =>
        (await InDocsAsync().ConfigureAwait(false))
            .Where(f => RepoRoot.IsPublished(f.Page))
            .ToList();

    /// <summary>
    /// Every fence a reader copies from: the published pages and the agent skills, which a release
    /// publishes on their own (Trax.Docs/adr/0056) and which carry the same markers.
    /// </summary>
    public static async Task<IReadOnlyList<Fence>> OnPagesAndSkillsAsync() =>
        (await InDocsAsync().ConfigureAwait(false))
            .Where(f => RepoRoot.IsPublished(f.Page) || RepoRoot.IsSkill(f.Page))
            .ToList();

    private static List<string> GroupsOf(IEnumerable<string> words, int openLine)
    {
        var groups = new List<string>();
        foreach (var word in words)
        {
            if (string.Equals(word, Marker, StringComparison.Ordinal))
                groups.Add(string.Create(CultureInfo.InvariantCulture, $"line {openLine}"));
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
