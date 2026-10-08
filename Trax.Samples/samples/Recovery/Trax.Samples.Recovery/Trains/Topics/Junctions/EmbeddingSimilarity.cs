using System.Text.RegularExpressions;
using Trax.Effect.Services.EffectJunction;

namespace Trax.Samples.Recovery.Trains.Topics.Junctions;

/// <summary>
/// The <c>embedding</c> branch: how alike every two papers read. A stand-in for an embedding model,
/// deterministic and without one: the cosine of the two papers' word counts over title and abstract.
/// </summary>
public partial class EmbeddingSimilarity(DemoPace pace)
    : EffectJunction<CorpusSlice, EmbeddingSignal>
{
    // Words that say nothing about a topic.
    private static readonly HashSet<string> Stopwords =
    [
        "after",
        "alone",
        "arrive",
        "before",
        "better",
        "each",
        "every",
        "from",
        "more",
        "most",
        "once",
        "show",
        "than",
        "that",
        "their",
        "them",
        "they",
        "this",
        "those",
        "with",
        "within",
        "where",
        "when",
        "which",
        "while",
        "year",
    ];

    public override async Task<EmbeddingSignal> Run(CorpusSlice slice)
    {
        await Task.Delay(pace.StepDelay, CancellationToken);

        var vectors = slice.Papers.ToDictionary(p => p.Id, p => Vector($"{p.Title} {p.Abstract}"));

        return new EmbeddingSignal(
            PaperPairs
                .Of(slice.Papers)
                .Select(pair => new PairSignal(
                    pair.A.Id,
                    pair.B.Id,
                    Math.Round(Cosine(vectors[pair.A.Id], vectors[pair.B.Id]), 3)
                ))
                .ToList()
        );
    }

    /// <summary>The paper's word counts: lower case, four letters or more, a plural's "s" dropped.</summary>
    public static Dictionary<string, int> Vector(string text)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var raw in NotALetter().Split(text.ToLowerInvariant()))
        {
            var word = raw.Length > 4 && raw.EndsWith('s') && !raw.EndsWith("ss") ? raw[..^1] : raw;
            if (word.Length < 4 || Stopwords.Contains(word))
                continue;
            counts[word] = counts.GetValueOrDefault(word) + 1;
        }
        return counts;
    }

    [GeneratedRegex("[^a-z]+")]
    private static partial Regex NotALetter();

    private static double Cosine(Dictionary<string, int> a, Dictionary<string, int> b)
    {
        double dot = 0;
        foreach (var (word, count) in a)
            if (b.TryGetValue(word, out var other))
                dot += count * other;

        var norm = Math.Sqrt(a.Values.Sum(v => v * v)) * Math.Sqrt(b.Values.Sum(v => v * v));
        return norm == 0 ? 0 : dot / norm;
    }
}
