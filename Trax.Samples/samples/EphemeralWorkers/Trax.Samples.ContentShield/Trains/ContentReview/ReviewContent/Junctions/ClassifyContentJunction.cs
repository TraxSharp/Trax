using Microsoft.Extensions.Logging;
using Trax.Core.Junction;

namespace Trax.Samples.ContentShield.Trains.ContentReview.ReviewContent.Junctions;

/// <summary>
/// Classifies content into a category (safe, spam, hate-speech, violence, etc.)
/// using a simulated ML model.
/// </summary>
public class ClassifyContentJunction(ILogger<ClassifyContentJunction> logger)
    : Junction<ReviewContentInput, ReviewContentInput>
{
    public override async Task<ReviewContentInput> Run(ReviewContentInput input)
    {
        logger.LogInformation(
            "Classifying {ContentType} content {ContentId}",
            input.ContentType,
            input.ContentId
        );

        await Task.Delay(300);

        var category = input.ContentType == "video" ? "violence" : "safe";
        logger.LogInformation(
            "Content {ContentId} classified as: {Category}",
            input.ContentId,
            category
        );

        return input;
    }
}
