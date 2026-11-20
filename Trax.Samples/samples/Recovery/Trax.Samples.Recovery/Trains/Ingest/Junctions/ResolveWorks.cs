using Trax.Effect.Services.EffectJunction;
using Trax.Samples.Recovery.Index;

namespace Trax.Samples.Recovery.Trains.Ingest.Junctions;

/// <summary>
/// The step on each track of the <c>SameWorkAsExisting</c> gate: what happens to the works with a
/// close match in the corpus. A work without one is created on every track. It only decides; the
/// upsert after it writes.
/// </summary>
public abstract class ResolveWorks(DemoPace pace, string matched, bool unsure)
    : EffectJunction<MatchEvidence, ResolvedPartition>
{
    public override async Task<ResolvedPartition> Run(MatchEvidence evidence)
    {
        await Task.Delay(pace.StepDelay, CancellationToken).ConfigureAwait(false);

        var matches = evidence.Candidates.ToDictionary(c => c.SourceId, StringComparer.Ordinal);
        var works = evidence
            .Works.Select(work =>
                !string.Equals(matched, Resolutions.Created, StringComparison.Ordinal)
                && matches.TryGetValue(work.SourceId, out var match)
                    ? new ResolvedWork(work, matched, match.ExistingWorkId)
                    : new ResolvedWork(work, Resolutions.Created, null)
            )
            .ToList();

        return new ResolvedPartition(evidence.Source, evidence.Month, unsure, works);
    }
}
