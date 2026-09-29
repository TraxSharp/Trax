using LanguageExt;
using Trax.Core.Junction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Services.EffectJunction;

/// <summary>
/// The contract <see cref="EffectJunction{TIn,TOut}"/> implements. Infrastructure; not intended to be implemented or
/// called directly.
/// </summary>
/// <typeparam name="TIn">The junction's input type.</typeparam>
/// <typeparam name="TOut">The junction's output type.</typeparam>
internal interface IEffectJunction<TIn, TOut> : IJunction<TIn, TOut>
{
    /// <summary>
    /// Runs the junction inside <paramref name="serviceTrain"/>, with its junction effects around it.
    /// </summary>
    /// <param name="previousOutput">The previous junction's result, or the train input.</param>
    /// <param name="serviceTrain">The running train.</param>
    public Task<Either<Exception, TOut>> RailwayJunction<TTrainIn, TTrainOut>(
        Either<Exception, TIn> previousOutput,
        ServiceTrain<TTrainIn, TTrainOut> serviceTrain
    );
}
