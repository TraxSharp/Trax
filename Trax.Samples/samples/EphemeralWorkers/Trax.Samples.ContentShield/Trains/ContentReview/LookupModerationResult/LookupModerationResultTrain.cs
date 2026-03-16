using LanguageExt;
using Trax.Effect.Attributes;
using Trax.Effect.Services.ServiceTrain;
using Trax.Samples.ContentShield.Trains.ContentReview.LookupModerationResult.Junctions;

namespace Trax.Samples.ContentShield.Trains.ContentReview.LookupModerationResult;

/// <summary>
/// Lightweight lookup of a content moderation result. Runs synchronously on the
/// API process — does not go through the scheduler or ephemeral Runner.
/// </summary>
[TraxQuery(Description = "Looks up a content moderation result")]
[TraxBroadcast]
public class LookupModerationResultTrain
    : ServiceTrain<LookupModerationResultInput, ModerationResult>,
        ILookupModerationResultTrain
{
    protected override async Task<Either<Exception, ModerationResult>> RunInternal(
        LookupModerationResultInput input
    ) => Activate(input).Chain<FetchModerationResultJunction>().Resolve();
}
