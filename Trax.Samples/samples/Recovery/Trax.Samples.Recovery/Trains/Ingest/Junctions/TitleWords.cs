using System.Text.RegularExpressions;

namespace Trax.Samples.Recovery.Trains.Ingest.Junctions;

/// <summary>The words of a title, lower case, and how many two titles share.</summary>
internal static partial class TitleWords
{
    public static HashSet<string> Of(string title) =>
        Word().Matches(title.ToLowerInvariant()).Select(m => m.Value).ToHashSet();

    /// <summary>The words both titles hold, over the words either holds.</summary>
    public static double Jaccard(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 && b.Count == 0)
            return 0;
        var shared = a.Count(b.Contains);
        return (double)shared / (a.Count + b.Count - shared);
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+")]
    private static partial Regex Word();
}
