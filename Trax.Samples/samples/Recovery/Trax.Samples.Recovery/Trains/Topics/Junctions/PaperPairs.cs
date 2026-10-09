namespace Trax.Samples.Recovery.Trains.Topics.Junctions;

/// <summary>Every two papers of a slice, each pair once, the lower id first.</summary>
internal static class PaperPairs
{
    public static IEnumerable<(Paper A, Paper B)> Of(IReadOnlyList<Paper> papers)
    {
        var ordered = papers.OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
        for (var i = 0; i < ordered.Count; i++)
        for (var j = i + 1; j < ordered.Count; j++)
            yield return (ordered[i], ordered[j]);
    }

    /// <summary>How many values the two lists hold in common.</summary>
    public static int Shared(IReadOnlyList<string> a, IReadOnlyList<string> b) =>
        a.Intersect(b, StringComparer.Ordinal).Count();
}
