using Microsoft.EntityFrameworkCore;
using Trax.Effect.Services.EffectJunction;
using Trax.Samples.Recovery.Corpus;

namespace Trax.Samples.Recovery.Trains.Ingest.Junctions;

/// <summary>
/// For each work, the corpus work whose title is closest, when it is close enough to ask about:
/// a share of title words in common (Jaccard) of <see cref="Threshold"/> or more.
/// </summary>
public class FindExistingMatches(ITopicMapDbContext db, DemoPace pace)
    : EffectJunction<NormalisedPartition, MatchEvidence>
{
    /// <summary>How alike two titles must be before the model is asked whether they are one work.</summary>
    public const double Threshold = 0.5;

    public override async Task<MatchEvidence> Run(NormalisedPartition partition)
    {
        await Task.Delay(pace.StepDelay, CancellationToken);

        var corpus = await db
            .Works.AsNoTracking()
            .OrderBy(w => w.Id)
            .Select(w => new { w.Id, w.Title })
            .ToListAsync(CancellationToken);

        var candidates = new List<WorkMatch>();
        foreach (var work in partition.Works)
        {
            var words = TitleWords.Of(work.Title);
            var best = corpus
                .Select(existing => new WorkMatch(
                    work.SourceId,
                    existing.Id,
                    existing.Title,
                    Math.Round(TitleWords.Jaccard(words, TitleWords.Of(existing.Title)), 3)
                ))
                .OrderByDescending(m => m.Similarity)
                .ThenBy(m => m.ExistingWorkId, StringComparer.Ordinal)
                .FirstOrDefault();

            if (best is not null && best.Similarity >= Threshold)
                candidates.Add(best);
        }

        return new MatchEvidence(partition.Source, partition.Month, partition.Works, candidates);
    }
}
