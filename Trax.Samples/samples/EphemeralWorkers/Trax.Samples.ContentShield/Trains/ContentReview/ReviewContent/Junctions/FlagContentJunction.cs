using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.Samples.ContentShield.Trains.ContentReview.ReviewContent.Junctions;

/// <summary>
/// Evaluates the threat score and flags content that exceeds the threshold.
/// </summary>
public class FlagContentJunction(ILogger<FlagContentJunction> logger)
    : Junction<ReviewContentInput, ReviewContentOutput>
{
    public override async Task<ReviewContentOutput> Run(ReviewContentInput input)
    {
        // Simulate threat scoring — video content scores high, text scores low
        var threatScore = input.ContentType == "video" ? 0.85 : 0.25;
        var classification = threatScore > 0.7 ? "violence" : "safe";
        var isFlagged = threatScore > 0.7;
        string? flagReason = isFlagged ? "Threat score exceeds moderation threshold" : null;

        if (isFlagged)
        {
            logger.LogWarning(
                "Content {ContentId} FLAGGED — score {ThreatScore:F2}, classification: {Classification}",
                input.ContentId,
                threatScore,
                classification
            );
        }
        else
        {
            logger.LogInformation(
                "Content {ContentId} passed moderation — score {ThreatScore:F2}",
                input.ContentId,
                threatScore
            );
        }

        return new ReviewContentOutput
        {
            ContentId = input.ContentId,
            Classification = classification,
            ThreatScore = threatScore,
            IsFlagged = isFlagged,
            FlagReason = flagReason,
        };
    }
}
