using Trax.Effect.Services.ServiceTrain;
using Trax.Core.Step;
using LanguageExt;

namespace Trax.Effect.Services.EffectStep;

public interface IEffectStep<TIn, TOut> : IStep<TIn, TOut>
{
    public Task<Either<Exception, TOut>> RailwayStep<TTrainIn, TTrainOut>(
        Either<Exception, TIn> previousOutput,
        ServiceTrain<TTrainIn, TTrainOut> serviceTrain
    );
}
