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
        await Task.Delay(pace.StepDelay, CancellationToken);

        var matches = evidence.Candidates.ToDictionary(c => c.SourceId);
        var works = evidence
            .Works.Select(work =>
                matched != Resolutions.Created && matches.TryGetValue(work.SourceId, out var match)
                    ? new ResolvedWork(work, matched, match.ExistingWorkId)
                    : new ResolvedWork(work, Resolutions.Created, null)
            )
            .ToList();

        return new ResolvedPartition(evidence.Source, evidence.Month, unsure, works);
    }
}

/// <summary>The <c>Yes</c> track: each close match is the corpus work it matches, and is linked to it.</summary>
public class MergeIntoExisting(DemoPace pace)
    : ResolveWorks(pace, Resolutions.Merged, unsure: false);

/// <summary>The <c>No</c> track: the close matches are different papers, and are created as new.</summary>
public class CreateAsNew(DemoPace pace) : ResolveWorks(pace, Resolutions.Created, unsure: false);

/// <summary>
/// The <c>Unsure</c> track: the close matches are kept aside for a person, with the corpus work
/// each might be.
/// </summary>
public class HoldForReview(DemoPace pace)
    : ResolveWorks(pace, Resolutions.NeedsReview, unsure: true);
