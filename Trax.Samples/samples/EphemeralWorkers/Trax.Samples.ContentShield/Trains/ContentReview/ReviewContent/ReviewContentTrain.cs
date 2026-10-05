using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.ContentShield.Trains.ContentReview.ReviewContent.Junctions;

namespace Trax.Samples.ContentShield.Trains.ContentReview.ReviewContent;

/// <summary>
/// Reviews user-submitted content for policy violations. Classifies the content,
/// scores its threat level, and flags violations. It starts no other train: a
/// moderator decides whether to send a violation notice. Starting the Moderator-gated
/// SendViolationNotice from this anonymous train would let anyone run it.
///
/// Dispatched to the ephemeral Runner via HTTP (UseRemoteWorkers).
///
/// Not [TraxBroadcast]: anyone may review content, and a broadcast train's lifecycle events carry
/// every run's output to every subscriber the train admits, so every review's result would go to
/// everyone listening.
/// </summary>
[TraxConcurrencyLimit(15)]
[TraxAllowAnonymous]
[TraxMutation(
    GraphQLOperation.Queue,
    Namespace = "moderation",
    Description = "Reviews content for policy violations"
)]
public class ReviewContentTrain
    : ServiceTrain<ReviewContentInput, ReviewContentOutput>,
        IReviewContentTrain
{
    protected override Task<Either<Exception, ReviewContentOutput>> Junctions() =>
        Chain<ClassifyContentJunction>()
            .Chain<ScoreContentJunction>()
            .Chain<FlagContentJunction>()
            .Resolve();
}
