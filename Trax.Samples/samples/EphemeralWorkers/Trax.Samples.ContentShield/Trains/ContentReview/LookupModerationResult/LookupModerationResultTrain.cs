using Trax.Core.Functional;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.ContentShield.Trains.ContentReview.LookupModerationResult.Junctions;

namespace Trax.Samples.ContentShield.Trains.ContentReview.LookupModerationResult;

/// <summary>
/// Lightweight lookup of a content moderation result. Runs synchronously, and like
/// every query it is sent to the Runner (UseRemoteRun); it does not go through the
/// scheduler.
///
/// Not [TraxBroadcast]: a query needs no lifecycle events, and an anonymous train's would carry
/// every lookup's result to every subscriber.
/// </summary>
[TraxAllowAnonymous]
[TraxQuery(Namespace = "moderation", Description = "Looks up a content moderation result")]
public class LookupModerationResultTrain
    : ServiceTrain<LookupModerationResultInput, ModerationResult>,
        ILookupModerationResultTrain
{
    protected override Task<Either<Exception, ModerationResult>> Junctions() =>
        Chain<FetchModerationResultJunction>().Resolve();
}
