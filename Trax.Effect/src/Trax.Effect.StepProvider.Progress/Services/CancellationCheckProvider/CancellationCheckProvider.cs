using Microsoft.EntityFrameworkCore;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Services.EffectStep;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Effect.StepProvider.Progress.Services.CancellationCheckProvider;

public class CancellationCheckProvider(IDataContextProviderFactory dataContextFactory)
    : ICancellationCheckProvider
{
    public async Task BeforeStepExecution<TIn, TOut, TWorkflowIn, TWorkflowOut>(
        EffectStep<TIn, TOut> effectStep,
        ServiceTrain<TWorkflowIn, TWorkflowOut> serviceTrain,
        CancellationToken cancellationToken
    )
    {
        if (serviceTrain.Metadata is null)
            return;

        await using var context = await dataContextFactory.CreateDbContextAsync(cancellationToken);

        var cancelRequested = await context
            .Metadatas.Where(m => m.Id == serviceTrain.Metadata.Id)
            .Select(m => m.CancellationRequested)
            .FirstOrDefaultAsync(cancellationToken);

        if (cancelRequested)
            throw new OperationCanceledException("Workflow cancellation requested via dashboard.");
    }

    public Task AfterStepExecution<TIn, TOut, TWorkflowIn, TWorkflowOut>(
        EffectStep<TIn, TOut> effectStep,
        ServiceTrain<TWorkflowIn, TWorkflowOut> serviceTrain,
        CancellationToken cancellationToken
    ) => Task.CompletedTask;

    public void Dispose() { }
}
