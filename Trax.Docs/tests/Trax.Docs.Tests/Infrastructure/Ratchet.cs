namespace Trax.Docs.Tests.Infrastructure;

/// <summary>
/// A list of pages exempt from a rule because they predate it, kept in a text file beside the
/// test: one docs-relative page path per line, in ordinal order, <c>#</c> lines are comments.
/// The list only shrinks. A listed page the rule no longer catches, or that no longer exists,
/// is stale and fails the test until its line is deleted.
/// </summary>
internal static class Ratchet
{
    /// <summary>The entries of the list at <paramref name="file"/> (docs-relative), as written.</summary>
    public static List<string> Entries(string file) =>
        File.ReadAllLines(RepoRoot.Combine(file.Split('/')))
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .ToList();

    /// <summary>
    /// The entries no longer needed: each one <paramref name="caught"/> does not hold, with why.
    /// </summary>
    public static List<string> Stale(IEnumerable<string> entries, IReadOnlySet<string> caught) =>
        entries
            .Where(e => !caught.Contains(e))
            .Select(e =>
                File.Exists(RepoRoot.Combine(e.Split('/')))
                    ? $"{e}  (the rule no longer catches it)"
                    : $"{e}  (no such page)"
            )
            .ToList();

    /// <summary>Whether the entries are unique and in ordinal order.</summary>
    public static bool IsSortedAndUnique(IReadOnlyList<string> entries) =>
        entries.SequenceEqual(
            entries.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal),
            StringComparer.Ordinal
        );
}
