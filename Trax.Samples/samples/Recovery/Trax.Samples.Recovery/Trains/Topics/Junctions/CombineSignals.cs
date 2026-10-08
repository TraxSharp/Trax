using Microsoft.EntityFrameworkCore;
using Trax.Effect.Services.EffectJunction;
using Trax.Samples.Recovery.Corpus;

namespace Trax.Samples.Recovery.Trains.Topics.Junctions;

/// <summary>
/// The join: weighs the three signals together for every two papers and writes the pairs that
/// score <see cref="Threshold"/> or more. The only step of the train that writes, in one
/// transaction, so a run either leaves all its pairs or none. A run written again (a requeue)
/// replaces its own pairs.
/// </summary>
public class CombineSignals(IDbContextFactory<TopicMapDbContext> contexts, DemoPace pace)
    : EffectJunction<
        (CorpusSlice, EmbeddingSignal, CoCitationSignal, AuthorSignal),
        CombinedSignals
    >
{
    /// <summary>The score a pair needs to be on the map.</summary>
    public const double Threshold = 0.3;

    public override async Task<CombinedSignals> Run(
        (CorpusSlice, EmbeddingSignal, CoCitationSignal, AuthorSignal) signals
    )
    {
        var (slice, embedding, coCitation, authors) = signals;
        await Task.Delay(pace.StepDelay, CancellationToken);

        var titles = slice.Papers.ToDictionary(p => p.Id, p => p.Title);
        var references = coCitation.Pairs.ToDictionary(p => (p.WorkA, p.WorkB), p => (int)p.Value);
        var coauthors = authors.Pairs.ToDictionary(p => (p.WorkA, p.WorkB), p => (int)p.Value);

        var links = embedding
            .Pairs.Select(pair =>
            {
                var shared = references.GetValueOrDefault((pair.WorkA, pair.WorkB));
                var sharedAuthors = coauthors.GetValueOrDefault((pair.WorkA, pair.WorkB));
                var score =
                    0.5 * pair.Value
                    + 0.3 * coCitation.Weight * Math.Min(shared / 2.0, 1)
                    + 0.2 * Math.Min(sharedAuthors, 1);
                return new TopicLink(
                    pair.WorkA,
                    titles[pair.WorkA],
                    pair.WorkB,
                    titles[pair.WorkB],
                    pair.Value,
                    shared,
                    sharedAuthors,
                    Math.Round(score, 3)
                );
            })
            .ToList();

        var onTheMap = links.Where(l => l.Score >= Threshold).ToList();

        await using var db = await contexts.CreateDbContextAsync(CancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(CancellationToken);

        await db
            .TopicPairs.Where(p => p.RunId == slice.RunId)
            .ExecuteDeleteAsync(CancellationToken);
        db.TopicPairs.AddRange(
            onTheMap.Select(l => new TopicPair
            {
                RunId = slice.RunId,
                WorkA = l.WorkA,
                WorkB = l.WorkB,
                Embedding = l.Embedding,
                SharedReferences = l.SharedReferences,
                SharedAuthors = l.SharedAuthors,
                Score = l.Score,
            })
        );
        await db.SaveChangesAsync(CancellationToken);
        await transaction.CommitAsync(CancellationToken);

        return new CombinedSignals(
            slice.RunId,
            slice.Papers.Count,
            coCitation.Track,
            onTheMap.Count,
            links
        );
    }
}
