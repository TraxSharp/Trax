using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Trax.Docs.Snippets.Tests.Infrastructure;

/// <summary>
/// The fences of one page that compile together: one fence marked <c>compile</c>, or every fence
/// on the page that names the same <c>compile=group</c>.
///
/// <para>
/// A page whose front matter says <c>follows: book/an-order-is-a-train</c> carries on a project the
/// page it names started, as a chapter of the book carries on the last one. Each of its groups
/// compiles with the followed page's group of the same name, and that page's own followed page,
/// back to the first. A fence that names a file (<c>file=Program.cs</c>) replaces the fence before
/// it that names the same file, so a chapter shows only the files it adds or changes and still
/// compiles as the whole project.
/// </para>
/// <para>
/// A group named <c>tests</c> is the project's test project. It compiles as an assembly of its own,
/// with the implicit usings of a plain <c>Microsoft.NET.Sdk</c> project and the test frameworks a
/// test project has (<see cref="SnippetCompiler.TestFrameworkAssemblyPrefixes"/>), against the
/// page's <c>app</c> group compiled as the project it references, whose internals it sees
/// (<see cref="App"/>).
/// </para>
/// </summary>
public sealed record SnippetUnit(string Page, string Group, IReadOnlyList<Fence> Fences)
{
    /// <summary>
    /// For a <see cref="TestsGroup"/> unit, the fences of the project it tests, which it references;
    /// empty otherwise.
    /// </summary>
    public IReadOnlyList<Fence> App { get; init; } = [];

    /// <summary>The group a page's test project is in.</summary>
    public const string TestsGroup = "tests";

    /// <summary>The group a <see cref="TestsGroup"/> compiles against.</summary>
    public const string AppGroup = "app";

    public override string ToString() => $"{Page} [{Group}]";

    /// <summary>Whether this unit is a test project, compiled with the test frameworks.</summary>
    public bool IsTests => string.Equals(Group, TestsGroup, StringComparison.Ordinal);

    /// <summary>The compiled fences of every page, one unit per page and group.</summary>
    public static IEnumerable<SnippetUnit> InDocs() =>
        WithTheAppInTheTests(
            Following(
                    Own(Infrastructure.Fences.InDocs()).ToList(),
                    SnippetCompiler.FollowedPageOf,
                    SnippetCompiler.MovesOf
                )
                .ToList(),
            SnippetCompiler.FollowedPageOf
        );

    /// <summary>
    /// Each <see cref="TestsGroup"/> unit with the <see cref="AppGroup"/> fences of its page, or of
    /// the nearest page before it in its chain that has them, as the project it tests; every other
    /// unit as it is.
    /// </summary>
    public static IEnumerable<SnippetUnit> WithTheAppInTheTests(
        IReadOnlyList<SnippetUnit> units,
        Func<string, string?>? followedPageOf = null
    )
    {
        var apps = units
            .Where(u => string.Equals(u.Group, AppGroup, StringComparison.Ordinal))
            .ToDictionary(u => u.Page, StringComparer.Ordinal);
        return units.Select(u =>
        {
            // A test project, or on a page in a chain, any project but the app: each is an assembly
            // of its own that references the app, as a book's worker references its library.
            var chained =
                followedPageOf is not null
                && (
                    followedPageOf(u.Page) is not null
                    || units.Any(o =>
                        string.Equals(followedPageOf(o.Page), u.Page, StringComparison.Ordinal)
                    )
                );
            if (
                string.Equals(u.Group, AppGroup, StringComparison.Ordinal)
                || !(u.IsTests || chained)
            )
                return u;
            var page = (string?)u.Page;
            while (page is not null && !apps.ContainsKey(page))
                page = followedPageOf?.Invoke(page);
            return page is null ? u : u with { App = apps[page].Fences };
        });
    }

    /// <summary>The units the fences make on their own pages, before any page follows another.</summary>
    public static IEnumerable<SnippetUnit> Own(IEnumerable<Fence> fences) =>
        fences
            .Where(f => f.IsCompiled && f.IsCSharp)
            .SelectMany(f => f.Groups.Select(g => (Fence: f, Group: g)))
            .GroupBy(x => (x.Fence.Page, x.Group))
            .OrderBy(g => g.Key.Page, StringComparer.Ordinal)
            .ThenBy(g => g.Key.Group, StringComparer.Ordinal)
            .Select(g => new SnippetUnit(
                g.Key.Page,
                g.Key.Group,
                g.Select(x => x.Fence).OrderBy(f => f.OpenLine).ToList()
            ));

    /// <summary>
    /// Each unit with the fences of the pages it follows, oldest first, a fence naming a file
    /// replacing an earlier one that names the same file.
    /// </summary>
    /// <param name="units">Every page's own units.</param>
    /// <param name="followedPageOf">The page a page follows, or null.</param>
    public static IEnumerable<SnippetUnit> Following(
        IReadOnlyList<SnippetUnit> units,
        Func<string, string?> followedPageOf,
        Func<string, IReadOnlyList<(string From, string To)>>? movesOf = null
    )
    {
        var byPage = units.ToDictionary(u => (u.Page, u.Group));

        foreach (var unit in units)
        {
            var chain = ChainOf(unit.Page, followedPageOf);
            if (chain.Count == 0)
            {
                yield return unit;
                continue;
            }

            var fences = new List<Fence>();
            foreach (var page in chain.Append(unit.Page))
            {
                // A page that moves files moves the ones the pages before it showed, before its own
                // fences replace any of them.
                foreach (var (from, to) in movesOf?.Invoke(page) ?? [])
                    for (var i = 0; i < fences.Count; i++)
                        if (
                            fences[i].File is { } file
                            && file.StartsWith(from, StringComparison.Ordinal)
                        )
                            fences[i] = fences[i] with { File = to + file[from.Length..] };
                if (!byPage.TryGetValue((page, unit.Group), out var earlier))
                    continue;
                foreach (var fence in earlier.Fences)
                {
                    if (fence.File is not null)
                        fences.RemoveAll(f =>
                            string.Equals(f.File, fence.File, StringComparison.Ordinal)
                        );
                    fences.Add(fence);
                }
            }

            yield return unit with
            {
                Fences = fences,
            };
        }
    }

    /// <summary>
    /// The pages <paramref name="page"/> follows, the first page of the chain first. Throws on a
    /// chain that comes back to a page it has passed.
    /// </summary>
    public static IReadOnlyList<string> ChainOf(string page, Func<string, string?> followedPageOf)
    {
        var chain = new List<string>();
        for (var at = followedPageOf(page); at is not null; at = followedPageOf(at))
        {
            if (string.Equals(at, page, StringComparison.Ordinal) || chain.Contains(at))
                throw new InvalidOperationException(
                    $"{page} follows a chain of pages that comes back to {at}."
                );
            chain.Add(at);
        }
        chain.Reverse();
        return chain;
    }
}
