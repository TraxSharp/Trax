using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Mediator.Services.WorkflowRegistry;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Effect.Services.ServiceTrain;
using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Schedule = Trax.Scheduler.Services.Scheduling.Schedule;

namespace Trax.Scheduler.Services.ManifestScheduler;

/// <summary>
/// Implementation of <see cref="IManifestScheduler"/> that provides type-safe manifest scheduling.
/// </summary>
public class ManifestScheduler(
    IDataContextProviderFactory dataContextFactory,
    IWorkflowRegistry workflowRegistry,
    ILogger<ManifestScheduler> logger
) : IManifestScheduler
{
    /// <inheritdoc />
    public async Task<Manifest> ScheduleAsync<TWorkflow, TInput>(
        string externalId,
        TInput input,
        Schedule schedule,
        Action<ScheduleOptions>? options = null,
        CancellationToken ct = default
    )
        where TWorkflow : IServiceTrain<TInput, Unit>
        where TInput : IManifestProperties
    {
        workflowRegistry.ValidateWorkflowRegistration<TInput>();

        var resolved = ResolveOptions(options);

        await using var context = CreateContext();

        var manifest = await context.UpsertManifestAsync<TWorkflow, TInput>(
            externalId,
            input,
            schedule,
            resolved.ManifestOptions,
            groupId: resolved.GroupId ?? externalId,
            groupPriority: resolved.GroupPriority,
            groupMaxActiveJobs: resolved.GroupMaxActiveJobs,
            groupIsEnabled: resolved.GroupEnabled,
            ct: ct
        );

        await context.SaveChanges(ct);

        logger.LogInformation(
            "Scheduled workflow {Workflow} with ExternalId {ExternalId}",
            typeof(TWorkflow).Name,
            externalId
        );

        return manifest;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Manifest>> ScheduleManyAsync<TWorkflow, TInput, TSource>(
        IEnumerable<TSource> sources,
        Func<TSource, (string ExternalId, TInput Input)> map,
        Schedule schedule,
        Action<ScheduleOptions>? options = null,
        Action<TSource, ManifestOptions>? configureEach = null,
        CancellationToken ct = default
    )
        where TWorkflow : IServiceTrain<TInput, Unit>
        where TInput : IManifestProperties
    {
        workflowRegistry.ValidateWorkflowRegistration<TInput>();

        var resolved = ResolveOptions(options);
        var sourceList = sources.ToList();

        if (sourceList.Count == 0)
            return [];

        await using var context = CreateContext();
        var transaction = await context.BeginTransaction();

        try
        {
            var effectiveGroupId =
                resolved.GroupId
                ?? resolved.PrunePrefix
                ?? sourceList.Select(s => map(s).ExternalId).FirstOrDefault()
                ?? "batch";

            var results = new List<Manifest>(sourceList.Count);

            foreach (var source in sourceList)
            {
                var (externalId, input) = map(source);
                var itemOptions = CreateItemOptions(resolved.ManifestOptions);
                configureEach?.Invoke(source, itemOptions);

                var manifest = await context.UpsertManifestAsync<TWorkflow, TInput>(
                    externalId,
                    input,
                    schedule,
                    itemOptions,
                    groupId: effectiveGroupId,
                    groupPriority: resolved.GroupPriority,
                    groupMaxActiveJobs: resolved.GroupMaxActiveJobs,
                    groupIsEnabled: resolved.GroupEnabled,
                    ct: ct
                );
                results.Add(manifest);
            }

            await context.SaveChanges(ct);

            if (resolved.PrunePrefix is not null)
            {
                var keepIds = results.Select(m => m.ExternalId).ToHashSet();
                await PruneStaleManifestsAsync(context, resolved.PrunePrefix, keepIds, ct);
            }

            await context.CommitTransaction();

            logger.LogInformation(
                "Scheduled {Count} manifests for workflow {Workflow} in single transaction",
                results.Count,
                typeof(TWorkflow).Name
            );

            return results;
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

    /// <inheritdoc />
    public async Task<Manifest> ScheduleDependentAsync<TWorkflow, TInput>(
        string externalId,
        TInput input,
        string dependsOnExternalId,
        Action<ScheduleOptions>? options = null,
        CancellationToken ct = default
    )
        where TWorkflow : IServiceTrain<TInput, Unit>
        where TInput : IManifestProperties
    {
        workflowRegistry.ValidateWorkflowRegistration<TInput>();

        var resolved = ResolveOptions(options);

        await using var context = CreateContext();

        var parentManifest =
            await context.Manifests.FirstOrDefaultAsync(
                m => m.ExternalId == dependsOnExternalId,
                ct
            )
            ?? throw new InvalidOperationException(
                $"Parent manifest with ExternalId '{dependsOnExternalId}' not found. "
                    + "Ensure the parent manifest is scheduled before its dependents."
            );

        var manifest = await context.UpsertDependentManifestAsync<TWorkflow, TInput>(
            externalId,
            input,
            parentManifest.Id,
            resolved.ManifestOptions,
            groupId: resolved.GroupId ?? externalId,
            groupPriority: resolved.GroupPriority,
            groupMaxActiveJobs: resolved.GroupMaxActiveJobs,
            groupIsEnabled: resolved.GroupEnabled,
            ct: ct
        );

        await context.SaveChanges(ct);

        logger.LogInformation(
            "Scheduled dependent workflow {Workflow} with ExternalId {ExternalId} depending on {ParentExternalId}",
            typeof(TWorkflow).Name,
            externalId,
            dependsOnExternalId
        );

        return manifest;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Manifest>> ScheduleManyDependentAsync<
        TWorkflow,
        TInput,
        TSource
    >(
        IEnumerable<TSource> sources,
        Func<TSource, (string ExternalId, TInput Input)> map,
        Func<TSource, string> dependsOn,
        Action<ScheduleOptions>? options = null,
        Action<TSource, ManifestOptions>? configureEach = null,
        CancellationToken ct = default
    )
        where TWorkflow : IServiceTrain<TInput, Unit>
        where TInput : IManifestProperties
    {
        workflowRegistry.ValidateWorkflowRegistration<TInput>();

        var resolved = ResolveOptions(options);
        var sourceList = sources.ToList();

        if (sourceList.Count == 0)
            return [];

        await using var context = CreateContext();
        var transaction = await context.BeginTransaction();

        try
        {
            var effectiveGroupId =
                resolved.GroupId
                ?? resolved.PrunePrefix
                ?? sourceList.Select(s => map(s).ExternalId).FirstOrDefault()
                ?? "batch";

            // Resolve all parent manifests in one query
            var parentExternalIds = sourceList.Select(dependsOn).Distinct().ToList();
            var parentManifests = await context
                .Manifests.Where(m => parentExternalIds.Contains(m.ExternalId))
                .ToDictionaryAsync(m => m.ExternalId, ct);

            var results = new List<Manifest>(sourceList.Count);

            foreach (var source in sourceList)
            {
                var (externalId, input) = map(source);
                var parentExternalId = dependsOn(source);

                if (!parentManifests.TryGetValue(parentExternalId, out var parentManifest))
                    throw new InvalidOperationException(
                        $"Parent manifest with ExternalId '{parentExternalId}' not found. "
                            + "Ensure parent manifests are scheduled before their dependents."
                    );

                var itemOptions = CreateItemOptions(resolved.ManifestOptions);
                configureEach?.Invoke(source, itemOptions);

                var manifest = await context.UpsertDependentManifestAsync<TWorkflow, TInput>(
                    externalId,
                    input,
                    parentManifest.Id,
                    itemOptions,
                    groupId: effectiveGroupId,
                    groupPriority: resolved.GroupPriority,
                    groupMaxActiveJobs: resolved.GroupMaxActiveJobs,
                    groupIsEnabled: resolved.GroupEnabled,
                    ct: ct
                );
                results.Add(manifest);
            }

            await context.SaveChanges(ct);

            if (resolved.PrunePrefix is not null)
            {
                var keepIds = results.Select(m => m.ExternalId).ToHashSet();
                await PruneStaleManifestsAsync(context, resolved.PrunePrefix, keepIds, ct);
            }

            await context.CommitTransaction();

            logger.LogInformation(
                "Scheduled {Count} dependent manifests for workflow {Workflow} in single transaction",
                results.Count,
                typeof(TWorkflow).Name
            );

            return results;
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

    /// <inheritdoc />
    public async Task DisableAsync(string externalId, CancellationToken ct = default)
    {
        await using var context = CreateContext();

        var manifest = await GetManifestByExternalIdAsync(context, externalId, ct);
        manifest.IsEnabled = false;
        await context.SaveChanges(ct);

        logger.LogInformation("Disabled manifest {ExternalId}", externalId);
    }

    /// <inheritdoc />
    public async Task EnableAsync(string externalId, CancellationToken ct = default)
    {
        await using var context = CreateContext();

        var manifest = await GetManifestByExternalIdAsync(context, externalId, ct);
        manifest.IsEnabled = true;
        await context.SaveChanges(ct);

        logger.LogInformation("Enabled manifest {ExternalId}", externalId);
    }

    /// <inheritdoc />
    public async Task TriggerAsync(string externalId, CancellationToken ct = default)
    {
        await using var context = CreateContext();

        var manifest = await GetManifestByExternalIdAsync(context, externalId, ct);

        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                WorkflowName = manifest.Name,
                Input = manifest.Properties,
                InputTypeName = manifest.PropertyTypeName,
                ManifestId = manifest.Id,
                Priority = manifest.Priority,
            }
        );
        context.WorkQueues.Add(entry);
        await context.SaveChanges(ct);

        logger.LogInformation(
            "Queued manifest {ExternalId} for execution (WorkQueueId: {WorkQueueId})",
            externalId,
            entry.Id
        );
    }

    /// <inheritdoc />
    public async Task<int> TriggerGroupAsync(long groupId, CancellationToken ct = default)
    {
        await using var context = CreateContext();

        var manifests = await context
            .Manifests.AsNoTracking()
            .Where(
                m =>
                    m.ManifestGroupId == groupId
                    && m.IsEnabled
                    && m.ScheduleType != ScheduleType.Dependent
                    && m.ScheduleType != ScheduleType.DormantDependent
            )
            .ToListAsync(ct);

        if (manifests.Count == 0)
            return 0;

        foreach (var manifest in manifests)
        {
            var entry = WorkQueue.Create(
                new CreateWorkQueue
                {
                    WorkflowName = manifest.Name,
                    Input = manifest.Properties,
                    InputTypeName = manifest.PropertyTypeName,
                    ManifestId = manifest.Id,
                    Priority = manifest.Priority,
                }
            );
            context.WorkQueues.Add(entry);
        }

        await context.SaveChanges(ct);

        logger.LogInformation(
            "Queued {Count} manifests in group {GroupId} for execution",
            manifests.Count,
            groupId
        );

        return manifests.Count;
    }

    // ── Internal non-generic overloads (used by WorkflowConfigurator) ───

    internal async Task<Manifest> ScheduleAsyncUntyped(
        Type workflowType,
        Type inputType,
        string externalId,
        IManifestProperties input,
        Schedule schedule,
        Action<ScheduleOptions>? options = null,
        CancellationToken ct = default
    )
    {
        workflowRegistry.ValidateWorkflowRegistration(inputType);

        var resolved = ResolveOptions(options);

        await using var context = CreateContext();

        var manifest = await context.UpsertManifestAsync(
            workflowType,
            externalId,
            input,
            schedule,
            resolved.ManifestOptions,
            groupId: resolved.GroupId ?? externalId,
            groupPriority: resolved.GroupPriority,
            groupMaxActiveJobs: resolved.GroupMaxActiveJobs,
            groupIsEnabled: resolved.GroupEnabled,
            ct: ct
        );

        await context.SaveChanges(ct);

        logger.LogInformation(
            "Scheduled workflow {Workflow} with ExternalId {ExternalId}",
            workflowType.Name,
            externalId
        );

        return manifest;
    }

    internal async Task<Manifest> ScheduleDependentAsyncUntyped(
        Type workflowType,
        Type inputType,
        string externalId,
        IManifestProperties input,
        string dependsOnExternalId,
        Action<ScheduleOptions>? options = null,
        CancellationToken ct = default
    )
    {
        workflowRegistry.ValidateWorkflowRegistration(inputType);

        var resolved = ResolveOptions(options);

        await using var context = CreateContext();

        var parentManifest =
            await context.Manifests.FirstOrDefaultAsync(
                m => m.ExternalId == dependsOnExternalId,
                ct
            )
            ?? throw new InvalidOperationException(
                $"Parent manifest with ExternalId '{dependsOnExternalId}' not found. "
                    + "Ensure the parent manifest is scheduled before its dependents."
            );

        var manifest = await context.UpsertDependentManifestAsync(
            workflowType,
            externalId,
            input,
            parentManifest.Id,
            resolved.ManifestOptions,
            groupId: resolved.GroupId ?? externalId,
            groupPriority: resolved.GroupPriority,
            groupMaxActiveJobs: resolved.GroupMaxActiveJobs,
            groupIsEnabled: resolved.GroupEnabled,
            ct: ct
        );

        await context.SaveChanges(ct);

        logger.LogInformation(
            "Scheduled dependent workflow {Workflow} with ExternalId {ExternalId} depending on {ParentExternalId}",
            workflowType.Name,
            externalId,
            dependsOnExternalId
        );

        return manifest;
    }

    internal async Task<IReadOnlyList<Manifest>> ScheduleManyAsyncUntyped<TSource>(
        Type workflowType,
        Type inputType,
        IEnumerable<TSource> sources,
        Func<TSource, (string ExternalId, IManifestProperties Input)> map,
        Schedule schedule,
        Action<ScheduleOptions>? options = null,
        Action<TSource, ManifestOptions>? configureEach = null,
        CancellationToken ct = default
    )
    {
        workflowRegistry.ValidateWorkflowRegistration(inputType);

        var resolved = ResolveOptions(options);
        var sourceList = sources.ToList();

        if (sourceList.Count == 0)
            return [];

        await using var context = CreateContext();
        var transaction = await context.BeginTransaction();

        try
        {
            var effectiveGroupId =
                resolved.GroupId
                ?? resolved.PrunePrefix
                ?? sourceList.Select(s => map(s).ExternalId).FirstOrDefault()
                ?? "batch";

            var results = new List<Manifest>(sourceList.Count);

            foreach (var source in sourceList)
            {
                var (externalId, input) = map(source);
                var itemOptions = CreateItemOptions(resolved.ManifestOptions);
                configureEach?.Invoke(source, itemOptions);

                var manifest = await context.UpsertManifestAsync(
                    workflowType,
                    externalId,
                    input,
                    schedule,
                    itemOptions,
                    groupId: effectiveGroupId,
                    groupPriority: resolved.GroupPriority,
                    groupMaxActiveJobs: resolved.GroupMaxActiveJobs,
                    groupIsEnabled: resolved.GroupEnabled,
                    ct: ct
                );
                results.Add(manifest);
            }

            await context.SaveChanges(ct);

            if (resolved.PrunePrefix is not null)
            {
                var keepIds = results.Select(m => m.ExternalId).ToHashSet();
                await PruneStaleManifestsAsync(context, resolved.PrunePrefix, keepIds, ct);
            }

            await context.CommitTransaction();

            logger.LogInformation(
                "Scheduled {Count} manifests for workflow {Workflow} in single transaction",
                results.Count,
                workflowType.Name
            );

            return results;
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

    internal async Task<IReadOnlyList<Manifest>> ScheduleManyDependentAsyncUntyped<TSource>(
        Type workflowType,
        Type inputType,
        IEnumerable<TSource> sources,
        Func<TSource, (string ExternalId, IManifestProperties Input)> map,
        Func<TSource, string> dependsOn,
        Action<ScheduleOptions>? options = null,
        Action<TSource, ManifestOptions>? configureEach = null,
        CancellationToken ct = default
    )
    {
        workflowRegistry.ValidateWorkflowRegistration(inputType);

        var resolved = ResolveOptions(options);
        var sourceList = sources.ToList();

        if (sourceList.Count == 0)
            return [];

        await using var context = CreateContext();
        var transaction = await context.BeginTransaction();

        try
        {
            var effectiveGroupId =
                resolved.GroupId
                ?? resolved.PrunePrefix
                ?? sourceList.Select(s => map(s).ExternalId).FirstOrDefault()
                ?? "batch";

            // Resolve all parent manifests in one query
            var parentExternalIds = sourceList.Select(dependsOn).Distinct().ToList();
            var parentManifests = await context
                .Manifests.Where(m => parentExternalIds.Contains(m.ExternalId))
                .ToDictionaryAsync(m => m.ExternalId, ct);

            var results = new List<Manifest>(sourceList.Count);

            foreach (var source in sourceList)
            {
                var (externalId, input) = map(source);
                var parentExternalId = dependsOn(source);

                if (!parentManifests.TryGetValue(parentExternalId, out var parentManifest))
                    throw new InvalidOperationException(
                        $"Parent manifest with ExternalId '{parentExternalId}' not found. "
                            + "Ensure parent manifests are scheduled before their dependents."
                    );

                var itemOptions = CreateItemOptions(resolved.ManifestOptions);
                configureEach?.Invoke(source, itemOptions);

                var manifest = await context.UpsertDependentManifestAsync(
                    workflowType,
                    externalId,
                    input,
                    parentManifest.Id,
                    itemOptions,
                    groupId: effectiveGroupId,
                    groupPriority: resolved.GroupPriority,
                    groupMaxActiveJobs: resolved.GroupMaxActiveJobs,
                    groupIsEnabled: resolved.GroupEnabled,
                    ct: ct
                );
                results.Add(manifest);
            }

            await context.SaveChanges(ct);

            if (resolved.PrunePrefix is not null)
            {
                var keepIds = results.Select(m => m.ExternalId).ToHashSet();
                await PruneStaleManifestsAsync(context, resolved.PrunePrefix, keepIds, ct);
            }

            await context.CommitTransaction();

            logger.LogInformation(
                "Scheduled {Count} dependent manifests for workflow {Workflow} in single transaction",
                results.Count,
                workflowType.Name
            );

            return results;
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

    // ── Private helpers ──────────────────────────────────────────────────

    private IDataContext CreateContext() =>
        dataContextFactory.Create() as IDataContext
        ?? throw new InvalidOperationException("Failed to create data context");

    private static ResolvedOptions ResolveOptions(Action<ScheduleOptions>? options)
    {
        var opts = new ScheduleOptions();
        options?.Invoke(opts);

        var manifestOptions = opts.ToManifestOptions();

        return new ResolvedOptions(
            ManifestOptions: manifestOptions,
            GroupId: opts._groupId,
            GroupPriority: opts._groupOptions?._priority ?? manifestOptions.Priority,
            GroupMaxActiveJobs: opts._groupOptions?._maxActiveJobs,
            GroupEnabled: opts._groupOptions?._isEnabled ?? true,
            PrunePrefix: opts._prunePrefix
        );
    }

    private static ManifestOptions CreateItemOptions(ManifestOptions baseOptions) =>
        new()
        {
            Priority = baseOptions.Priority,
            IsEnabled = baseOptions.IsEnabled,
            MaxRetries = baseOptions.MaxRetries,
            Timeout = baseOptions.Timeout,
            IsDormant = baseOptions.IsDormant,
        };

    private static async Task<Manifest> GetManifestByExternalIdAsync(
        IDataContext context,
        string externalId,
        CancellationToken ct
    ) =>
        await context.Manifests.FirstOrDefaultAsync(m => m.ExternalId == externalId, ct)
        ?? throw new InvalidOperationException($"No manifest found with ExternalId '{externalId}'");

    private async Task PruneStaleManifestsAsync(
        IDataContext context,
        string prunePrefix,
        System.Collections.Generic.HashSet<string> keepExternalIds,
        CancellationToken ct
    )
    {
        var staleManifestIds = await context
            .Manifests.Where(
                m => m.ExternalId.StartsWith(prunePrefix) && !keepExternalIds.Contains(m.ExternalId)
            )
            .Select(m => m.Id)
            .ToListAsync(ct);

        if (staleManifestIds.Count == 0)
            return;

        // Delete in FK-dependency order: work_queue → dead_letters → metadata → manifests
        await context
            .WorkQueues.Where(
                w => w.ManifestId.HasValue && staleManifestIds.Contains(w.ManifestId.Value)
            )
            .ExecuteDeleteAsync(ct);

        await context
            .DeadLetters.Where(d => staleManifestIds.Contains(d.ManifestId))
            .ExecuteDeleteAsync(ct);

        await context
            .Metadatas.Where(
                m => m.ManifestId.HasValue && staleManifestIds.Contains(m.ManifestId.Value)
            )
            .ExecuteDeleteAsync(ct);

        var pruned = await context
            .Manifests.Where(m => staleManifestIds.Contains(m.Id))
            .ExecuteDeleteAsync(ct);

        logger.LogInformation(
            "Pruned {Count} stale manifests with prefix '{Prefix}'",
            pruned,
            prunePrefix
        );
    }

    private record ResolvedOptions(
        ManifestOptions ManifestOptions,
        string? GroupId,
        int GroupPriority,
        int? GroupMaxActiveJobs,
        bool GroupEnabled,
        string? PrunePrefix
    );
}
