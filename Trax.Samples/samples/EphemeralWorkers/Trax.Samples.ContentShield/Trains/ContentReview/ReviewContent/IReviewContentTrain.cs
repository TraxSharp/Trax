using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.ContentShield.Trains.ContentReview.ReviewContent;

public interface IReviewContentTrain : IServiceTrain<ReviewContentInput, ReviewContentOutput>;
