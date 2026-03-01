using LanguageExt;
using Microsoft.Extensions.Logging;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.EffectStep;
using Trax.Mediator.Services.WorkflowBus;
using Trax.Scheduler.Services.DormantDependentContext;

namespace Trax.Scheduler.Workflows.TaskServerExecutor.Steps;

/// <summary>
/// Executes the target workflow using the WorkflowBus with the resolved input.
/// </summary>
internal class ExecuteScheduledWorkflowStep(
    IWorkflowBus workflowBus,
    DormantDependentContext dormantDependentContext,
    ILogger<ExecuteScheduledWorkflowStep> logger
) : EffectStep<(Metadata, ResolvedWorkflowInput), Unit>
{
    public override async Task<Unit> Run((Metadata, ResolvedWorkflowInput) input)
    {
        var (metadata, resolvedInput) = input;

        // Initialize the dormant dependent context so user workflow steps
        // can activate dormant dependents of this parent manifest
        if (metadata.ManifestId.HasValue)
            dormantDependentContext.Initialize(metadata.ManifestId.Value);

        logger.LogDebug(
            "Executing workflow {WorkflowName} for Metadata {MetadataId}",
            metadata.Name,
            metadata.Id
        );

        await workflowBus.RunAsync(resolvedInput.Value, CancellationToken, metadata);

        logger.LogDebug(
            "Successfully executed workflow {WorkflowName} for Metadata {MetadataId}",
            metadata.Name,
            metadata.Id
        );

        return Unit.Default;
    }
}
