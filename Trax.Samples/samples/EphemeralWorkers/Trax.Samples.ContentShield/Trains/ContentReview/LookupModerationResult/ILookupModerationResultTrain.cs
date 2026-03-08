using Trax.Effect.Services.ServiceTrain;

namespace Trax.Samples.ContentShield.Trains.ContentReview.LookupModerationResult;

public interface ILookupModerationResultTrain
    : IServiceTrain<LookupModerationResultInput, ModerationResult>;
