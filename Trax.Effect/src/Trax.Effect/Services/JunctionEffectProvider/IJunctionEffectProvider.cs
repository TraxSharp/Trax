using Trax.Effect.Services.EffectJunction;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Services.JunctionEffectProvider;

/// <summary>
/// A junction effect: code that runs before and after every <see cref="EffectJunction{TIn,TOut}"/> in a service
/// train, such as the junction logger or junction progress. A new provider is created by its
/// <see cref="JunctionEffectProviderFactory.IJunctionEffectProviderFactory"/> for each train run and disposed when
/// the run ends. Register one with <c>AddJunctionEffect</c>.
/// </summary>
public interface IJunctionEffectProvider : IDisposable
{
    /// <summary>
    /// Called before the junction runs, after its <c>Metadata</c> is created and before its start time is set,
    /// including for a junction that is skipped because an earlier junction failed. Before a junction that will run,
    /// an exception propagates out of the junction and fails the train. Before a skipped one, the railway's failure is
    /// what the train reports: the exception is logged and the providers after this one do not run.
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
    /// Called after the junction runs, whether it succeeded, failed or was skipped because an earlier junction
    /// failed; <c>Metadata</c> then holds the end time and railway state. After a junction that succeeded, an
    /// exception propagates and fails the train. After one that failed or was skipped, the railway's failure is what
    /// the train reports: the exception is logged and the providers after this one do not run.
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
