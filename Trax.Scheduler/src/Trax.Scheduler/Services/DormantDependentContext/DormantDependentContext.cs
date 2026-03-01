using System.Text.Json;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Services.ServiceTrain;
using Trax.Effect.Utils;
using Trax.Scheduler.Configuration;

namespace Trax.Scheduler.Services.DormantDependentContext;

/// <summary>
/// Scoped implementation of <see cref="IDormantDependentContext"/> that creates WorkQueue
/// entries for dormant dependent manifests with runtime-provided input.
/// </summary>
/// <remarks>
/// Registered as Scoped so that each Hangfire job execution gets its own instance.
/// Must be initialized via <see cref="Initialize"/> before use — this is done automatically
/// by <c>ExecuteScheduledWorkflowStep</c> in the TaskServerExecutor pipeline.
/// </remarks>
internal class DormantDependentContext(
    IDataContextProviderFactory dataContextFactory,
    SchedulerConfiguration schedulerConfiguration,
    ILogger<DormantDependentContext> logger
) : IDormantDependentContext
{
    private long? _parentManifestId;

    /// <summary>
    /// Binds this context to the currently executing parent manifest.
    /// Called by <c>ExecuteScheduledWorkflowStep</c> before the user's workflow runs.
    /// </summary>
    /// <param name="parentManifestId">The database ID of the parent manifest.</param>
    internal void Initialize(long parentManifestId)
    {
        _parentManifestId = parentManifestId;
    }

    /// <inheritdoc />
    public async Task ActivateAsync<TWorkflow, TInput>(
        string externalId,
        TInput input,
        CancellationToken ct = default
    )
        where TWorkflow : IServiceTrain<TInput, Unit>
        where TInput : IManifestProperties
    {
        EnsureInitialized();

        await using var context = CreateContext();
        await ActivateSingleAsync<TInput>(context, externalId, input, ct);
        await context.SaveChanges(ct);
    }

    /// <inheritdoc />
    public async Task ActivateManyAsync<TWorkflow, TInput>(
        IEnumerable<(string ExternalId, TInput Input)> activations,
        CancellationToken ct = default
    )
        where TWorkflow : IServiceTrain<TInput, Unit>
        where TInput : IManifestProperties
    {
        EnsureInitialized();

        var activationList = activations.ToList();
        if (activationList.Count == 0)
            return;

        await using var context = CreateContext();
        var transaction = await context.BeginTransaction();

        try
        {
            foreach (var (externalId, input) in activationList)
                await ActivateSingleAsync<TInput>(context, externalId, input, ct);

            await context.SaveChanges(ct);
            await context.CommitTransaction();

            logger.LogInformation(
                "Activated {Count} dormant dependents for parent manifest {ParentManifestId}",
                activationList.Count,
                _parentManifestId
            );
        }
        catch
        {
            await context.RollbackTransaction();
            throw;
        }
        finally
        {
            transaction?.Dispose();
        }
    }

    /// <summary>
    /// Validates and creates a WorkQueue entry for a single dormant dependent manifest.
    /// </summary>
    private async Task ActivateSingleAsync<TInput>(
        IDataContext context,
        string externalId,
        TInput input,
        CancellationToken ct
    )
        where TInput : IManifestProperties
    {
        // Load manifest with its group for priority calculation
        var manifest =
            await context
                .Manifests.Include(m => m.ManifestGroup)
                .FirstOrDefaultAsync(m => m.ExternalId == externalId, ct)
            ?? throw new InvalidOperationException(
                $"No manifest found with ExternalId '{externalId}'"
            );

        // Validate it's a dormant dependent
        if (manifest.ScheduleType != ScheduleType.DormantDependent)
            throw new InvalidOperationException(
                $"Manifest '{externalId}' has ScheduleType {manifest.ScheduleType}, "
                    + "expected DormantDependent. Only dormant dependents can be activated "
                    + "via IDormantDependentContext."
            );

        // Validate parent relationship
        if (manifest.DependsOnManifestId != _parentManifestId)
            throw new InvalidOperationException(
                $"Manifest '{externalId}' depends on manifest {manifest.DependsOnManifestId}, "
                    + $"but the current parent is {_parentManifestId}. "
                    + "A dormant dependent can only be activated by its declared parent."
            );

        // Concurrency guard: check for existing queued work
        var hasQueuedWork = await context.WorkQueues.AnyAsync(
            w => w.ManifestId == manifest.Id && w.Status == WorkQueueStatus.Queued,
            ct
        );
        if (hasQueuedWork)
        {
            logger.LogWarning(
                "Skipping activation of dormant dependent '{ExternalId}' "
                    + "(ManifestId: {ManifestId}) — already has queued work",
                externalId,
                manifest.Id
            );
            return;
        }

        // Concurrency guard: check for active execution
        var hasActiveExecution = await context.Metadatas.AnyAsync(
            m =>
                m.ManifestId == manifest.Id
                && (
                    m.WorkflowState == WorkflowState.Pending
                    || m.WorkflowState == WorkflowState.InProgress
                ),
            ct
        );
        if (hasActiveExecution)
        {
            logger.LogWarning(
                "Skipping activation of dormant dependent '{ExternalId}' "
                    + "(ManifestId: {ManifestId}) — has active execution",
                externalId,
                manifest.Id
            );
            return;
        }

        // Calculate priority with DependentPriorityBoost
        var basePriority = manifest.ManifestGroup.Priority;
        var effectivePriority = basePriority + schedulerConfiguration.DependentPriorityBoost;

        // Serialize the runtime input
        var inputJson = JsonSerializer.Serialize(
            input,
            input.GetType(),
            TraxJsonSerializationOptions.ManifestProperties
        );

        var entry = Trax.Effect.Models.WorkQueue.WorkQueue.Create(
            new CreateWorkQueue
            {
                WorkflowName = manifest.Name,
                Input = inputJson,
                InputTypeName = typeof(TInput).FullName,
                ManifestId = manifest.Id,
                Priority = effectivePriority,
            }
        );

        await context.Track(entry);

        logger.LogInformation(
            "Activated dormant dependent '{ExternalId}' (ManifestId: {ManifestId}, "
                + "WorkQueueId: {WorkQueueId}, Priority: {Priority})",
            externalId,
            manifest.Id,
            entry.Id,
            effectivePriority
        );
    }

    private void EnsureInitialized()
    {
        if (_parentManifestId is null)
            throw new InvalidOperationException(
                "DormantDependentContext has not been initialized. "
                    + "This service can only be used within a scheduled workflow execution. "
                    + "Ensure the workflow is running via the scheduler, not invoked directly."
            );
    }

    private IDataContext CreateContext() =>
        dataContextFactory.Create() as IDataContext
        ?? throw new InvalidOperationException("Failed to create data context");
}
