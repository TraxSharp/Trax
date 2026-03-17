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
/// Registered as Scoped so that each job execution gets its own instance for scoped
/// dependencies (IDataContextProviderFactory, etc.). The parent manifest ID is stored
/// in a static <see cref="AsyncLocal{T}"/> so it flows from the JobRunner scope (where
/// <c>RunScheduledTrainJunction</c> calls <see cref="Initialize"/>) into the child scope
/// created by <see cref="Trax.Mediator.Services.TrainBus.TrainBus"/> for the user's train.
/// </remarks>
internal class DormantDependentContext(
    IDataContextProviderFactory dataContextFactory,
    SchedulerConfiguration schedulerConfiguration,
    ILogger<DormantDependentContext> logger
) : IDormantDependentContext
{
    private static readonly AsyncLocal<long?> ParentManifestIdLocal = new();

    private long? ParentManifestId => ParentManifestIdLocal.Value;

    /// <summary>
    /// Binds this context to the currently executing parent manifest.
    /// Called by <c>RunScheduledTrainJunction</c> before the user's train runs.
    /// The value is stored in an <see cref="AsyncLocal{T}"/> so it flows across DI scopes
    /// on the same async call stack (e.g., into the TrainBus's child scope).
    /// </summary>
    /// <param name="parentManifestId">The database ID of the parent manifest.</param>
    internal void Initialize(long parentManifestId)
    {
        ParentManifestIdLocal.Value = parentManifestId;
    }

    /// <summary>
    /// Clears the parent manifest ID from the async-local context.
    /// Called by <c>RunScheduledTrainJunction</c> after the user's train completes
    /// to prevent stale values from leaking into subsequent job executions on the same worker.
    /// </summary>
    internal void Reset()
    {
        ParentManifestIdLocal.Value = null;
    }

    /// <inheritdoc />
    public async Task ActivateAsync<TTrain, TInput, TOutput>(
        string externalId,
        TInput input,
        CancellationToken ct = default
    )
        where TTrain : IServiceTrain<TInput, TOutput>
        where TInput : IManifestProperties
    {
        if (!IsInitialized)
        {
            logger.LogWarning(
                "IDormantDependentContext.ActivateAsync('{ExternalId}') called outside of a "
                    + "scheduled execution — no parent manifest context. "
                    + "Activation skipped. This happens when the train is invoked directly "
                    + "(e.g. via GraphQL mutation or ITrainBus) instead of through the scheduler",
                externalId
            );
            return;
        }

        await using var context = CreateContext();
        await ActivateSingleAsync<TInput>(context, externalId, input, ct);
        await context.SaveChanges(ct);
    }

    /// <inheritdoc />
    public async Task ActivateManyAsync<TTrain, TInput, TOutput>(
        IEnumerable<(string ExternalId, TInput Input)> activations,
        CancellationToken ct = default
    )
        where TTrain : IServiceTrain<TInput, TOutput>
        where TInput : IManifestProperties
    {
        if (!IsInitialized)
        {
            logger.LogWarning(
                "IDormantDependentContext.ActivateManyAsync called outside of a "
                    + "scheduled execution — no parent manifest context. "
                    + "Activation skipped. This happens when the train is invoked directly "
                    + "(e.g. via GraphQL mutation or ITrainBus) instead of through the scheduler"
            );
            return;
        }

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
                ParentManifestId
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
        if (manifest.DependsOnManifestId != ParentManifestId)
            throw new InvalidOperationException(
                $"Manifest '{externalId}' depends on manifest {manifest.DependsOnManifestId}, "
                    + $"but the current parent is {ParentManifestId}. "
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
                && (m.TrainState == TrainState.Pending || m.TrainState == TrainState.InProgress),
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
                TrainName = manifest.Name,
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

    private bool IsInitialized => ParentManifestId is not null;

    private IDataContext CreateContext() =>
        dataContextFactory.Create() as IDataContext
        ?? throw new InvalidOperationException("Failed to create data context");
}
