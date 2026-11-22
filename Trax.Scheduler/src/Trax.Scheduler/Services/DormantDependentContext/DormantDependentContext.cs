using System.Globalization;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Core.Extensions;
using Trax.Core.Functional;
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
internal sealed class DormantDependentContext(
    IDataContextProviderFactory dataContextFactory,
    SchedulerConfiguration schedulerConfiguration,
    ILogger<DormantDependentContext> logger,
    TimeProvider timeProvider
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

        var context = CreateContext();
        await using (context.ConfigureAwait(false))
        {
            await ActivateSingleAsync(context, externalId, input, ct).ConfigureAwait(false);
            await context.SaveChanges(ct).ConfigureAwait(false);
        }
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

        var context = CreateContext();
        await using (context.ConfigureAwait(false))
        {
            var transaction = await context.BeginTransaction(ct).ConfigureAwait(false);

            try
            {
                foreach (var (externalId, input) in activationList)
                    await ActivateSingleAsync(context, externalId, input, ct).ConfigureAwait(false);

                await context.SaveChanges(ct).ConfigureAwait(false);
                await context.CommitTransaction().ConfigureAwait(false);

                logger.LogInformation(
                    "Activated {Count} dormant dependents for parent manifest {ParentManifestId}",
                    activationList.Count,
                    ParentManifestId
                );
            }
            catch
            {
                await context.RollbackTransaction().ConfigureAwait(false);
                throw;
            }
            finally
            {
                transaction?.Dispose();
            }
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
        var manifest = await LoadDormantDependentAsync(context, externalId, ct)
            .ConfigureAwait(false);

        // A disabled manifest, or one in a disabled group, stays stopped: its parent's success
        // does not restart it, just as the ManifestManager does not queue it.
        if (!manifest.IsEnabled || !manifest.ManifestGroup.NotNull().IsEnabled)
        {
            logger.LogWarning(
                "Skipping activation of dormant dependent '{ExternalId}' "
                    + "(ManifestId: {ManifestId}) — {Disabled} is disabled",
                externalId,
                manifest.Id,
                manifest.IsEnabled ? "its manifest group" : "the manifest"
            );
            return;
        }

        if (
            await HasQueuedOrActiveWorkAsync(context, manifest, externalId, ct)
                .ConfigureAwait(false)
        )
            return;

        // Calculate priority with DependentPriorityBoost
        var basePriority = manifest.ManifestGroup.Priority;
        var effectivePriority = basePriority + schedulerConfiguration.DependentPriorityBoost;

        var entry = CreateActivationEntry(manifest, input, effectivePriority);

        await context.Track(entry).ConfigureAwait(false);

        logger.LogInformation(
            "Activated dormant dependent '{ExternalId}' (ManifestId: {ManifestId}, "
                + "WorkQueueId: {WorkQueueId}, Priority: {Priority})",
            externalId,
            manifest.Id,
            entry.Id,
            effectivePriority
        );
    }

    private Trax.Effect.Models.WorkQueue.WorkQueue CreateActivationEntry<TInput>(
        Manifest manifest,
        TInput input,
        int effectivePriority
    )
        where TInput : IManifestProperties
    {
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
                StarterHash = manifest.StarterHash,
                Priority = effectivePriority,
            },
            timeProvider
        );

        return entry;
    }

    private async Task<bool> HasQueuedOrActiveWorkAsync(
        IDataContext context,
        Manifest manifest,
        string externalId,
        CancellationToken ct
    )
    {
        // Concurrency guard: check for existing queued work
        var hasQueuedWork = await context
            .WorkQueues.AnyAsync(
                w => w.ManifestId == manifest.Id && w.Status == WorkQueueStatus.Queued,
                ct
            )
            .ConfigureAwait(false);
        if (hasQueuedWork)
        {
            logger.LogWarning(
                "Skipping activation of dormant dependent '{ExternalId}' "
                    + "(ManifestId: {ManifestId}) — already has queued work",
                externalId,
                manifest.Id
            );
            return true;
        }

        // Concurrency guard: check for active execution
        var hasActiveExecution = await context
            .Metadatas.AnyAsync(
                m =>
                    m.ManifestId == manifest.Id
                    && (
                        m.TrainState == TrainState.Pending || m.TrainState == TrainState.InProgress
                    ),
                ct
            )
            .ConfigureAwait(false);
        if (hasActiveExecution)
        {
            logger.LogWarning(
                "Skipping activation of dormant dependent '{ExternalId}' "
                    + "(ManifestId: {ManifestId}) — has active execution",
                externalId,
                manifest.Id
            );
            return true;
        }

        return false;
    }

    private async Task<Manifest> LoadDormantDependentAsync(
        IDataContext context,
        string externalId,
        CancellationToken ct
    )
    {
        // Load manifest with its group for priority calculation
        var manifest =
            await context
                .Manifests.Include(m => m.ManifestGroup)
                .FirstOrDefaultAsync(m => m.ExternalId == externalId, ct)
                .ConfigureAwait(false)
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
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Manifest '{externalId}' depends on manifest {manifest.DependsOnManifestId}, "
                )
                    + string.Create(
                        CultureInfo.InvariantCulture,
                        $"but the current parent is {ParentManifestId}. "
                    )
                    + "A dormant dependent can only be activated by its declared parent."
            );

        return manifest;
    }

    private bool IsInitialized => ParentManifestId is not null;

    private IDataContext CreateContext() =>
        dataContextFactory.Create() as IDataContext
        ?? throw new InvalidOperationException("Failed to create data context");
}
