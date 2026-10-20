namespace Trax.Docs.Snippets.Tests.Infrastructure;

/// <summary>
/// The project the book builds, as it stands after its last page: every fence on the book's chain
/// of pages that names a file (<c>file=Program.cs</c>), in any language, the later fence for a file
/// replacing the earlier one and a page's <c>moves:</c> re-homing the files before it, as
/// <see cref="SnippetUnit.Following"/> does for the compiled fences.
/// </summary>
internal static class BookProject
{
    /// <summary>The book's pages, relative to the docs root.</summary>
    public const string Folder = "book/";

    /// <summary>
    /// The last page of the book's chain: the one page under <see cref="Folder"/> that follows
    /// another and that no page follows. Throws when there is not exactly one, which means the book
    /// has split into two chains.
    /// </summary>
    public static string LastPage()
    {
        var pages = RepoRoot
            .MarkdownFiles()
            .Select(f => RepoRoot.Relative(f).Replace('\\', '/'))
            .Where(p => p.StartsWith(Folder, StringComparison.Ordinal))
            .ToList();
        var followed = pages
            .Select(SnippetCompiler.FollowedPageOf)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        var last = pages
            .Where(p => SnippetCompiler.FollowedPageOf(p) is not null && !followed.Contains(p))
            .ToList();
        return last.Count == 1
            ? last[0]
            : throw new InvalidOperationException(
                $"The book should be one chain of pages with one last page; these end a chain: "
                    + string.Join(", ", last)
            );
    }

    /// <summary>Each file the book shows, by its path in the reader's project, and the fence that last shows it.</summary>
    public static IReadOnlyDictionary<string, Fence> Files()
    {
        var last = LastPage();
        var files = new Dictionary<string, Fence>(StringComparer.Ordinal);
        foreach (var page in SnippetUnit.ChainOf(last, SnippetCompiler.FollowedPageOf).Append(last))
        {
            foreach (var (from, to) in SnippetCompiler.MovesOf(page))
            foreach (
                var file in files
                    .Keys.Where(f => f.StartsWith(from, StringComparison.Ordinal))
                    .ToList()
            )
            {
                files[to + file[from.Length..]] = files[file];
                files.Remove(file);
            }

            var markdown = File.ReadAllText(RepoRoot.Combine(page.Split('/')));
            foreach (var fence in Fences.In(page, markdown).Where(f => f.File is not null))
                files[fence.File!] = fence;
        }
        return files;
    }
}
