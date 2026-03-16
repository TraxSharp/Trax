using LanguageExt;
using Trax.Core.Junction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Services.EffectJunction;

public interface IEffectJunction<TIn, TOut> : IJunction<TIn, TOut>
{
    public Task<Either<Exception, TOut>> RailwayJunction<TTrainIn, TTrainOut>(
        Either<Exception, TIn> previousOutput,
        ServiceTrain<TTrainIn, TTrainOut> serviceTrain
    );
}
