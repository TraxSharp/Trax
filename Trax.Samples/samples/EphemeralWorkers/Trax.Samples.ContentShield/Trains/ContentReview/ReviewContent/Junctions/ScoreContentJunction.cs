using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.Samples.ContentShield.Trains.ContentReview.ReviewContent.Junctions;

/// <summary>
/// Assigns a threat score (0.0–1.0) to the content based on classification signals.
/// Scores above 0.7 trigger flagging in the next step.
/// </summary>
public class ScoreContentJunction(ILogger<ScoreContentJunction> logger)
    : Junction<ReviewContentInput, ReviewContentInput>
{
    public override async Task<ReviewContentInput> Run(ReviewContentInput input)
    {
        logger.LogInformation("Scoring threat level for content {ContentId}", input.ContentId);

        await Task.Delay(200);

        var score = input.ContentType == "video" ? 0.85 : 0.25;
        logger.LogInformation(
            "Content {ContentId} threat score: {ThreatScore:F2}",
            input.ContentId,
            score
        );

        return input;
    }
}
