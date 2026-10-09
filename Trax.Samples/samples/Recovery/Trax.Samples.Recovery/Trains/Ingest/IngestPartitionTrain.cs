using Trax.Core.Functional;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Recovery.Trains.Ingest.Junctions;

namespace Trax.Samples.Recovery.Trains.Ingest;

/// <summary>
/// Ingests one partition of a scholarly index, a source and a month: read its records, normalise
/// them into one shape, find the works whose titles are close to ones the corpus already holds, ask
/// the model whether those are the same works, write every work, and sum up what was written as
/// counts and a fingerprint.
/// </summary>
/// <remarks>
/// <para>
/// Every step is an <c>EffectJunction</c>, and every step can run again: reading and deciding write
/// nothing, and <see cref="UpsertWorks"/> overwrites a partition's own rows. Running a partition
/// twice leaves the same rows and returns the same output.
/// </para>
/// <para>
/// The output holds counts and a fingerprint, never rows. When the model is unsure, the output
/// says so in <see cref="IngestPartitionResult.Unsure"/>, and the works it was unsure about are
/// written as needing review.
/// </para>
/// <para>
/// It carries no <c>[TraxAuthorize]</c> and no <c>[TraxBroadcast]</c>: it is not on the GraphQL
/// schema, nothing a user calls starts it, and nothing outside the operations view watches it.
/// </para>
/// </remarks>
public class IngestPartitionTrain
    : ServiceTrain<IngestPartitionInput, IngestPartitionResult>,
        IIngestPartitionTrain
{
    protected override Task<Either<Exception, IngestPartitionResult>> Junctions() =>
        Chain<FetchPartition>()
            .Chain<NormaliseWorks>()
            .Chain<FindExistingMatches>()
            .Gate<MatchEvidence, SameWorkAsExisting>(gate =>
                gate.Yes(t => t.Chain<MergeIntoExisting>(), atLeast: 0.8)
                    .No(t => t.Chain<CreateAsNew>(), below: 0.3)
                    .Unsure(t => t.Chain<HoldForReview>())
            )
            .Chain<UpsertWorks>()
            .Chain<FingerprintPartition>()
            .Resolve();
}
