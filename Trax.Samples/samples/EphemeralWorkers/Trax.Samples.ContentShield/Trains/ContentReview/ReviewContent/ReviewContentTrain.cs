using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.ContentShield.Trains.ContentReview.ReviewContent.Steps;

namespace Trax.Samples.ContentShield.Trains.ContentReview.ReviewContent;

/// <summary>
/// Reviews user-submitted content for policy violations. Classifies the content,
/// scores its threat level, and flags violations. When flagged, activates the
/// dormant SendViolationNotice train to notify the content owner.
///
/// Dispatched to the ephemeral Runner via HTTP (UseRemoteWorkers).
/// </summary>
[TraxConcurrencyLimit(15)]
[TraxMutation(GraphQLOperation.Queue, Description = "Reviews content for policy violations")]
[TraxBroadcast]
public class ReviewContentTrain
    : ServiceTrain<ReviewContentInput, ReviewContentOutput>,
        IReviewContentTrain
{
    protected override async Task<Either<Exception, ReviewContentOutput>> RunInternal(
        ReviewContentInput input
    ) =>
        Activate(input)
            .Chain<ClassifyContentStep>()
            .Chain<ScoreContentStep>()
            .Chain<FlagContentStep>()
            .Resolve();
}
