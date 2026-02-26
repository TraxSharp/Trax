using Trax.Effect.Services.EffectStep;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.Services.StepEffectRunner;

public interface IStepEffectRunner : IDisposable
{
    Task BeforeStepExecution<TIn, TOut, TWorkflowIn, TWorkflowOut>(
        EffectStep<TIn, TOut> effectStep,
        ServiceTrain<TWorkflowIn, TWorkflowOut> serviceTrain,
        CancellationToken cancellationToken
    );

    Task AfterStepExecution<TIn, TOut, TWorkflowIn, TWorkflowOut>(
        EffectStep<TIn, TOut> effectStep,
        ServiceTrain<TWorkflowIn, TWorkflowOut> serviceTrain,
        CancellationToken cancellationToken
    );
}
