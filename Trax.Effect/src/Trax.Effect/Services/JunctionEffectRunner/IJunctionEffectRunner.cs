using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.JunctionEffectProvider;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Services.JunctionEffectRunner;

/// <summary>
/// Runs every enabled junction effect around a junction. A service train gets one per run through its
/// <c>[Inject]</c> <c>JunctionEffectRunner</c> property; infrastructure, not intended to be called directly.
/// </summary>
public interface IJunctionEffectRunner : IDisposable
{
    /// <summary>
    /// Calls <see cref="IJunctionEffectProvider.BeforeJunctionExecution{TIn,TOut,TTrainIn,TTrainOut}"/> on each
    /// active provider in registration order.
    /// </summary>
    /// <param name="effectJunction">The junction being run; its <c>Metadata</c> is already created.</param>
    /// <param name="serviceTrain">The train running the junction.</param>
    /// <param name="cancellationToken">The train's cancellation token.</param>
    Task BeforeJunctionExecution<TIn, TOut, TTrainIn, TTrainOut>(
        EffectJunction<TIn, TOut> effectJunction,
        ServiceTrain<TTrainIn, TTrainOut> serviceTrain,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Calls <see cref="IJunctionEffectProvider.AfterJunctionExecution{TIn,TOut,TTrainIn,TTrainOut}"/> on each
    /// active provider in registration order.
    /// </summary>
    /// <param name="effectJunction">The junction being run; its <c>Metadata</c> is already created.</param>
    /// <param name="serviceTrain">The train running the junction.</param>
    /// <param name="cancellationToken">The train's cancellation token.</param>
    Task AfterJunctionExecution<TIn, TOut, TTrainIn, TTrainOut>(
        EffectJunction<TIn, TOut> effectJunction,
        ServiceTrain<TTrainIn, TTrainOut> serviceTrain,
        CancellationToken cancellationToken
    );
}
