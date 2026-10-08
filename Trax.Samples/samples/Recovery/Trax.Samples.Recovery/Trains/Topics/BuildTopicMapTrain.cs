using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.Recovery.Auth;
using Trax.Samples.Recovery.Trains.Topics.Junctions;

namespace Trax.Samples.Recovery.Trains.Topics;

/// <summary>
/// A topic map of a slice of papers: load the slice, work out three similarity signals side by side
/// (how alike the abstracts read, which works both cite, who wrote both), weigh them together and
/// write the pairs, then find the papers that read alike but cite nothing in common. The co-citation
/// branch asks the model whether shared references mean a shared topic in this slice; the step on the
/// track it chooses is the one the page can crash.
/// </summary>
/// <remarks>
/// Branches compute; the join commits. Each branch runs in its own scope, so only
/// <see cref="CombineSignals"/>, after every branch has finished, writes to the database, in one
/// transaction. A failed branch fails the run before anything is written.
/// </remarks>
[TraxBroadcast]
[TraxAuthorize(Roles = RecoveryRoles.Operator + "," + RecoveryRoles.Viewer)]
public class BuildTopicMapTrain : ServiceTrain<TopicMapInput, TopicMap>, IBuildTopicMapTrain
{
    protected override Task<Either<Exception, TopicMap>> Junctions() =>
        Chain<LoadCorpus>()
            .Parallel(signals =>
                signals
                    .Branch("embedding", b => b.Chain<EmbeddingSimilarity>())
                    .Branch(
                        "cocitation",
                        b =>
                            b.Chain<CountSharedReferences>()
                                .Gate<CoCitationEvidence, SameTopic>(gate =>
                                    gate.Yes(t => t.Chain<TrustCoCitation>(), atLeast: 0.8)
                                        .No(t => t.Chain<IgnoreCoCitation>(), below: 0.3)
                                        .Unsure(t => t.Chain<DampenCoCitation>())
                                )
                    )
                    .Branch("authors", b => b.Chain<AuthorOverlap>())
            )
            .Chain<CombineSignals>()
            .Chain<FindHiddenTwins>()
            .Resolve();
}
