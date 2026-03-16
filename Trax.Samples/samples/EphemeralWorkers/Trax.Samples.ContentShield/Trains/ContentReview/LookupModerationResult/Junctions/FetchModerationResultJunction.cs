using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.Samples.ContentShield.Trains.ContentReview.LookupModerationResult.Junctions;

/// <summary>
/// Fetches a moderation result from the database. Returns a simulated result
/// for demonstration purposes.
/// </summary>
public class FetchModerationResultJunction(ILogger<FetchModerationResultJunction> logger)
    : Junction<LookupModerationResultInput, ModerationResult>
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
