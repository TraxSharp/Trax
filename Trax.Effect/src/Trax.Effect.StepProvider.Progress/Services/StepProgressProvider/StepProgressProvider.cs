using Trax.Effect.Services.EffectStep;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.StepProvider.Progress.Services.StepProgressProvider;

public class StepProgressProvider : IStepProgressProvider
{
    public async Task BeforeStepExecution<TIn, TOut, TWorkflowIn, TWorkflowOut>(
        EffectStep<TIn, TOut> effectStep,
        ServiceTrain<TWorkflowIn, TWorkflowOut> serviceTrain,
        CancellationToken cancellationToken
    )
    {
        if (serviceTrain.Metadata is null || serviceTrain.EffectRunner is null)
            return;

        serviceTrain.Metadata.CurrentlyRunningStep = effectStep.Metadata?.Name;
        serviceTrain.Metadata.StepStartedAt = DateTime.UtcNow;

        await serviceTrain.EffectRunner.Update(serviceTrain.Metadata);
        await serviceTrain.EffectRunner.SaveChanges(cancellationToken);
    }

    public async Task AfterStepExecution<TIn, TOut, TWorkflowIn, TWorkflowOut>(
        EffectStep<TIn, TOut> effectStep,
        ServiceTrain<TWorkflowIn, TWorkflowOut> serviceTrain,
        CancellationToken cancellationToken
    )
    {
        if (serviceTrain.Metadata is null || serviceTrain.EffectRunner is null)
            return;

        serviceTrain.Metadata.CurrentlyRunningStep = null;
        serviceTrain.Metadata.StepStartedAt = null;

        await serviceTrain.EffectRunner.Update(serviceTrain.Metadata);
        await serviceTrain.EffectRunner.SaveChanges(cancellationToken);
    }

    public void Dispose() { }
}
