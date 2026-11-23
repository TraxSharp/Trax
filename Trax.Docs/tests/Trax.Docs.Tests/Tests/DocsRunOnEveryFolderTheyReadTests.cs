using System.Text.Json;

namespace Trax.Docs.Tests.Tests;

/// <summary>
/// CI runs the docs tests whenever a folder they read changes.
///
/// <para>The docs tests read code outside Trax.Docs: a page's <c>source:</c> files, the files its
/// <c>from=</c> fences quote, and every C# file's <c>&lt;seealso href&gt;</c> links. CI runs a
/// folder's tests only when it or a folder upstream of it changes
/// (<c>.github/ci/packages.json</c>, closed over by <c>.github/scripts/ci-changes.sh</c>). A folder
/// the docs read but that is not upstream of Trax.Docs can break a page in a pull request that
/// never runs the test that would say so.</para>
///
/// <para>Enforces <c>Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md</c>.</para>
/// </summary>
[Property(
    "adr",
    "Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md"
)]
[TestFixture]
public partial class DocsRunOnEveryFolderTheyReadTests
{
    private const string Adr =
        "Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md";

    private const string Config = ".github/ci/packages.json";

    [GeneratedRegex(
        @"^\s*(```|~~~).*\bfrom=(?<path>[^\s#]+)",
        RegexOptions.Multiline | RegexOptions.ExplicitCapture,
        matchTimeoutMilliseconds: 1000
    )]
    private static partial Regex FromMarker { get; }

    [Test]
    public async Task Every_FolderTheDocsRead_IsUpstreamOfTraxDocs()
    {
        var packages = await PackagesAsync().ConfigureAwait(false);
        var upstream = Closure("Trax.Docs", packages);
        var folders = packages.Keys.ToHashSet(StringComparer.Ordinal);

        var offenders = (await FoldersReadAsync(folders).ConfigureAwait(false))
            .Where(read =>
                !string.Equals(read.Folder, "Trax.Docs", StringComparison.Ordinal)
                && !upstream.Contains(read.Folder)
            )
            .Select(read => $"{read.Folder} ({read.Why})")
            .Order(StringComparer.Ordinal)
            .ToList();

        offenders
            .Should()
            .BeEmpty(
                $"every folder the docs tests read must be upstream of Trax.Docs in {Config}, or "
                    + "a change to it skips the docs tests and can break a page unnoticed. Add the "
                    + "folder to Trax.Docs's `upstream`. See "
                    + Adr
                    + ". Not upstream:\n  "
                    + string.Join("\n  ", offenders)
            );
    }

    [Test]
    public void UpstreamOf_TakesTheClosure()
    {
        var packages = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["A"] = [],
            ["B"] = ["A"],
            ["C"] = ["B"],
            ["D"] = ["C", "A"],
        };

        Closure("D", packages).Should().BeEquivalentTo(["A", "B", "C"]);
    }

    [Test]
    public void FromMarker_ReadsThePathWithoutTheRegion()
    {
        const string page = "```csharp from=Trax.Samples/samples/X/Program.cs#docs-setup\n```\n";

        FromMarker
            .Match(page)
            .Groups["path"]
            .Value.Should()
            .Be("Trax.Samples/samples/X/Program.cs");
    }

    /// <summary>The folders the docs tests read, with the first reason found for each.</summary>
    private static async Task<IEnumerable<(string Folder, string Why)>> FoldersReadAsync(
        IReadOnlySet<string> folders
    )
    {
        var why = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var page in RepoRoot.PublishedPages())
        {
            var markdown = await File.ReadAllTextAsync(RepoRoot.Combine(page.Split('/')))
                .ConfigureAwait(false);
            foreach (var path in FrontMatter.Of(markdown).List("source"))
                why.TryAdd(TopFolder(path), $"`source:` of {page}");
            foreach (Match m in FromMarker.Matches(markdown))
                why.TryAdd(TopFolder(m.Groups["path"].Value), $"`from=` fence on {page}");
        }

        // CodeLinksToDocsResolveTests reads the <seealso href> links of every C# file.
        foreach (var folder in folders)
        {
            var root = Path.Combine(RepoRoot.Monorepo, folder);
            if (RepoRoot.FilesUnder(root, "*.cs").Any())
                why.TryAdd(folder, "its C# files' docs links");
        }

        return why.Where(kv => folders.Contains(kv.Key)).Select(kv => (kv.Key, kv.Value));
    }

    private static string TopFolder(string repoRelative) =>
        repoRelative.TrimStart('/').Split('/')[0];

    private static IReadOnlySet<string> Closure(
        string folder,
        IReadOnlyDictionary<string, string[]> packages
    )
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(packages[folder]);
        while (pending.TryPop(out var next))
        {
            if (!seen.Add(next))
                continue;
            foreach (var up in packages[next])
                pending.Push(up);
        }
        return seen;
    }

    private static async Task<IReadOnlyDictionary<string, string[]>> PackagesAsync()
    {
        var file = Path.Combine([RepoRoot.Monorepo, .. Config.Split('/')]);
        using var json = JsonDocument.Parse(
            await File.ReadAllTextAsync(file).ConfigureAwait(false)
        );
        return json
            .RootElement.GetProperty("packages")
            .EnumerateArray()
            .ToDictionary(
                p => Text(p.GetProperty("folder")),
                p => p.GetProperty("upstream").EnumerateArray().Select(Text).ToArray(),
                StringComparer.Ordinal
            );
    }

    private static string Text(JsonElement element) =>
        element.GetString()
        ?? throw new InvalidOperationException($"{Config} holds a null where a string belongs.");
}
