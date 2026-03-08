using Microsoft.Extensions.Logging;
using Trax.Core.Step;

namespace Trax.Samples.ContentShield.Trains.ContentReview.LookupModerationResult.Steps;

/// <summary>
/// Fetches a moderation result from the database. Returns a simulated result
/// for demonstration purposes.
/// </summary>
public class FetchModerationResultStep(ILogger<FetchModerationResultStep> logger)
    : Step<LookupModerationResultInput, ModerationResult>
{
    public override async Task<ModerationResult> Run(LookupModerationResultInput input)
    {
        logger.LogInformation(
            "Fetching moderation result for content {ContentId}",
            input.ContentId
        );

        await Task.Delay(50);

        return new ModerationResult
        {
            ContentId = input.ContentId,
            ModerationStatus = "Reviewed",
            Classification = "safe",
            ThreatScore = 0.12,
            ReviewedAt = DateTimeOffset.UtcNow.AddMinutes(-15),
        };
    }
}
