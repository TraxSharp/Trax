namespace Trax.Docs.Snippets.Tests.Infrastructure;

/// <summary>
/// A fenced code block in a markdown page, with where it sits and what its info string asks for.
/// </summary>
/// <param name="Page">The page, relative to the docs root, with forward slashes.</param>
/// <param name="OpenLine">The 1-based line of the opening fence.</param>
/// <param name="Language">The first word of the info string, empty when there is none.</param>
/// <param name="Groups">
/// The compilations this fence joins. Empty when the fence is not marked <c>compile</c>. A bare
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
    /// <summary>Whether the info string carries <c>fragment</c>.</summary>
    public bool IsFragment { get; init; }

    /// <summary>
    /// The file a <c>from=</c> marker names, relative to the repository root (the folder holding
    /// Trax.Docs). Null when the fence carries no <c>from=</c>.
    /// </summary>
    public string? From { get; init; }

    /// <summary>The region a <c>from=path#region</c> marker names. Null for the whole file.</summary>
    public string? FromRegion { get; init; }

    /// <summary>
    /// The file a <c>file=</c> word names: the path of the file the fence shows, in the reader's
    /// project. A page that follows another compiles with that page's fences, and a fence here
    /// replaces the one there that names the same file. Null when the fence names none.
    /// </summary>
    public string? File { get; init; }

    /// <summary>
    /// The text of the last level 1 or level 2 heading above the fence, or null when there is
    /// none. A level 3 heading does not change it, so a fence under <c>### Runtime</c> inside
    /// <c>## Signatures</c> is still a signature fence.
    /// </summary>
    public string? Section { get; init; }

    /// <summary>The 1-based line of the fence's first line of code.</summary>
    public int FirstCodeLine => OpenLine + 1;

    public bool IsCompiled => Groups.Count > 0;

    /// <summary>
    /// The markers the fence carries: <c>compile</c>, <c>fragment</c> and <c>from=</c>, each
    /// at most once however many groups it names.
    /// </summary>
    public IReadOnlyList<string> Markers
    {
        get
        {
            var markers = new List<string>();
            if (IsCompiled)
                markers.Add(Fences.Marker);
            if (IsFragment)
                markers.Add(Fences.FragmentMarker);
            if (From is not null)
                markers.Add(Fences.FromMarker);
            return markers;
        }
    }

    public bool IsCSharp => Language is "csharp" or "cs" or "c#";

    /// <summary>Whether the fence sits under a <c>## Signature</c> or <c>## Signatures</c> heading.</summary>
    public bool IsUnderSignature => Section is "Signature" or "Signatures";

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
public static class Fences
{
    public const string Marker = "compile";

    public const string FragmentMarker = "fragment";

    public const string FromMarker = "from=";

    /// <summary>
    /// Names the file a compiled fence shows. It is not a marker: it says which file, not how the
    /// fence relates to the code, so it goes beside <c>compile</c> rather than instead of it.
    /// </summary>
    public const string FileWord = "file=";

    private static readonly Regex Opening = new(
        @"^(?<indent>[ \t]*)(?<fence>`{3,}|~{3,})(?<info>.*)$",
        RegexOptions.Compiled
    );

    private static readonly Regex Heading = new(
        @"^ {0,3}#{1,2}[ \t]+(?<text>.*?)(?:[ \t]+#+)?[ \t]*$",
        RegexOptions.Compiled
    );

    public static IEnumerable<Fence> In(string page, string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
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
            var markers = words.Skip(1).ToList();
            var from = markers
                .FirstOrDefault(w => w.StartsWith(FromMarker, StringComparison.Ordinal))
                ?[FromMarker.Length..];
            var hash = from?.IndexOf('#') ?? -1;

            yield return new Fence(
                page,
                i + 1,
                language,
                GroupsOf(markers, i + 1),
                string.Join("\n", body)
            )
            {
                IsFragment = markers.Contains(FragmentMarker, StringComparer.Ordinal),
                From = hash < 0 ? from : from![..hash],
                FromRegion = hash < 0 ? null : from![(hash + 1)..],
                File = markers
                    .FirstOrDefault(w => w.StartsWith(FileWord, StringComparison.Ordinal))
                    ?[FileWord.Length..],
                Section = section,
            };
            i = j;
        }
    }

    /// <summary>Every fence in every markdown file under the docs root, published or not.</summary>
    public static IEnumerable<Fence> InDocs() =>
        RepoRoot
            .MarkdownFiles()
            .SelectMany(file =>
                In(RepoRoot.Relative(file).Replace('\\', '/'), File.ReadAllText(file))
            );

    /// <summary>Every fence on a page traxsharp.net publishes.</summary>
    public static IEnumerable<Fence> OnPublishedPages() =>
        InDocs().Where(f => RepoRoot.IsPublished(f.Page));

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
