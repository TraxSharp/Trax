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
public class DocsRunOnEveryFolderTheyReadTests
{
    private const string Adr =
        "Trax.Docs/adr/0050-docs-pages-are-typed-and-their-code-is-checked-against-the-code.md";

    private const string Config = ".github/ci/packages.json";

    private static readonly Regex FromMarker = new(
        @"^\s*(```|~~~).*\bfrom=(?<path>[^\s#]+)",
        RegexOptions.Compiled | RegexOptions.Multiline
    );

    [Test]
    public void Every_FolderTheDocsRead_IsUpstreamOfTraxDocs()
    {
        var upstream = UpstreamOf("Trax.Docs");
        var folders = Folders();

        var offenders = FoldersRead(folders)
            .Where(read => read.Folder != "Trax.Docs" && !upstream.Contains(read.Folder))
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
        var packages = new Dictionary<string, string[]>
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
    private static IEnumerable<(string Folder, string Why)> FoldersRead(
        IReadOnlySet<string> folders
    )
    {
        var why = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var page in RepoRoot.PublishedPages())
        {
            var markdown = File.ReadAllText(RepoRoot.Combine(page.Split('/')));
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

    private static IReadOnlySet<string> Folders() =>
        Packages().Keys.ToHashSet(StringComparer.Ordinal);

    private static IReadOnlySet<string> UpstreamOf(string folder) => Closure(folder, Packages());

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

    private static IReadOnlyDictionary<string, string[]> Packages()
    {
        var file = Path.Combine([RepoRoot.Monorepo, .. Config.Split('/')]);
        using var json = JsonDocument.Parse(File.ReadAllText(file));
        return json
            .RootElement.GetProperty("packages")
            .EnumerateArray()
            .ToDictionary(
                p => p.GetProperty("folder").GetString()!,
                p =>
                    p.GetProperty("upstream")
                        .EnumerateArray()
                        .Select(u => u.GetString()!)
                        .ToArray(),
                StringComparer.Ordinal
            );
    }
}
