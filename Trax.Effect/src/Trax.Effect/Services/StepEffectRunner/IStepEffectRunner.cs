using Trax.Effect.Services.EffectStep;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Services.StepEffectRunner;

public interface IStepEffectRunner : IDisposable
{
    Task BeforeStepExecution<TIn, TOut, TTrainIn, TTrainOut>(
        EffectStep<TIn, TOut> effectStep,
        ServiceTrain<TTrainIn, TTrainOut> serviceTrain,
        CancellationToken cancellationToken
    );

    Task AfterStepExecution<TIn, TOut, TTrainIn, TTrainOut>(
        EffectStep<TIn, TOut> effectStep,
        ServiceTrain<TTrainIn, TTrainOut> serviceTrain,
        CancellationToken cancellationToken
    );
}
