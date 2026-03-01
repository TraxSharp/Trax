using LanguageExt;
using Trax.Core.Step;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Services.EffectStep;

public interface IEffectStep<TIn, TOut> : IStep<TIn, TOut>
{
    public Task<Either<Exception, TOut>> RailwayStep<TTrainIn, TTrainOut>(
        Either<Exception, TIn> previousOutput,
        ServiceTrain<TTrainIn, TTrainOut> serviceTrain
    );
}
