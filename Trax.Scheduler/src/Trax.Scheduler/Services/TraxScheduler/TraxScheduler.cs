using LanguageExt;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Services.ChangeSignal;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainRegistry;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.CancellationRegistry;
using Schedule = Trax.Scheduler.Services.Scheduling.Schedule;

namespace Trax.Scheduler.Services.TraxScheduler;

/// <summary>
/// Implementation of <see cref="ITraxScheduler"/> that provides type-safe manifest scheduling.
/// </summary>
public class TraxScheduler(
    IDataContextProviderFactory dataContextFactory,
    ITrainRegistry trainRegistry,
    ICancellationRegistry cancellationRegistry,
    ILogger<TraxScheduler> logger,
    // Optional so direct construction in tests stays simple; always resolved via DI in a host.
    ITraxChangeSignal? changeSignal = null
) : ITraxScheduler
{
    /// <inheritdoc />
    public async Task<Manifest> ScheduleAsync<TTrain, TInput, TOutput>(
        string externalId,
        TInput input,
        Schedule schedule,
        Action<ScheduleOptions>? options = null,
        CancellationToken ct = default
    )
        where TTrain : IServiceTrain<TInput, TOutput>
        where TInput : IManifestProperties
    {
        trainRegistry.ValidateTrainRegistration<TInput>();

        var resolved = ResolveOptions(options);

        await using var context = CreateContext();

        var manifest = await context.UpsertManifestAsync<TTrain, TInput, TOutput>(
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
            "Scheduled train {Train} with ExternalId {ExternalId}",
            typeof(TTrain).Name,
            externalId
        );

        return manifest;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Manifest>> ScheduleManyAsync<TTrain, TInput, TOutput, TSource>(
        IEnumerable<TSource> sources,
        Func<TSource, (string ExternalId, TInput Input)> map,
        Schedule schedule,
        Action<ScheduleOptions>? options = null,
        Action<TSource, ManifestOptions>? configureEach = null,
        CancellationToken ct = default
    )
        where TTrain : IServiceTrain<TInput, TOutput>
        where TInput : IManifestProperties
    {
        trainRegistry.ValidateTrainRegistration<TInput>();

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

                var manifest = await context.UpsertManifestAsync<TTrain, TInput, TOutput>(
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
            await context.CommitTransaction();

            logger.LogInformation(
                "Scheduled {Count} manifests for train {Train} in single transaction",
                results.Count,
                typeof(TTrain).Name
            );

            if (resolved.PrunePrefix is not null)
            {
                var keepIds = results.Select(m => m.ExternalId).ToHashSet();
                await PruneSafeAsync(resolved.PrunePrefix, keepIds, ct);
            }

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
    public async Task<Manifest> ScheduleDependentAsync<TTrain, TInput, TOutput>(
        string externalId,
        TInput input,
        string dependsOnExternalId,
        Action<ScheduleOptions>? options = null,
        CancellationToken ct = default
    )
        where TTrain : IServiceTrain<TInput, TOutput>
        where TInput : IManifestProperties
    {
        trainRegistry.ValidateTrainRegistration<TInput>();

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

        var manifest = await context.UpsertDependentManifestAsync<TTrain, TInput, TOutput>(
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
            "Scheduled dependent train {Train} with ExternalId {ExternalId} depending on {ParentExternalId}",
            typeof(TTrain).Name,
            externalId,
            dependsOnExternalId
        );

        return manifest;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<Manifest>> ScheduleManyDependentAsync<
        TTrain,
        TInput,
        TOutput,
        TSource
    >(
        IEnumerable<TSource> sources,
        Func<TSource, (string ExternalId, TInput Input)> map,
        Func<TSource, string> dependsOn,
        Action<ScheduleOptions>? options = null,
        Action<TSource, ManifestOptions>? configureEach = null,
        CancellationToken ct = default
    )
        where TTrain : IServiceTrain<TInput, TOutput>
        where TInput : IManifestProperties
    {
        trainRegistry.ValidateTrainRegistration<TInput>();

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

                var manifest = await context.UpsertDependentManifestAsync<TTrain, TInput, TOutput>(
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
            await context.CommitTransaction();

            logger.LogInformation(
                "Scheduled {Count} dependent manifests for train {Train} in single transaction",
                results.Count,
                typeof(TTrain).Name
            );

            if (resolved.PrunePrefix is not null)
            {
                var keepIds = results.Select(m => m.ExternalId).ToHashSet();
                await PruneSafeAsync(resolved.PrunePrefix, keepIds, ct);
            }

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
        changeSignal?.Notify(ChangeDomain.Manifest);

        logger.LogInformation("Disabled manifest {ExternalId}", externalId);
    }

    /// <inheritdoc />
    public async Task EnableAsync(string externalId, CancellationToken ct = default)
    {
        await using var context = CreateContext();

        var manifest = await GetManifestByExternalIdAsync(context, externalId, ct);
        manifest.IsEnabled = true;
        await context.SaveChanges(ct);
        changeSignal?.Notify(ChangeDomain.Manifest);

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
                TrainName = manifest.Name,
                Input = manifest.Properties,
                InputTypeName = manifest.PropertyTypeName,
                ManifestId = manifest.Id,
                Priority = manifest.Priority,
            }
        );
        context.WorkQueues.Add(entry);
        await context.SaveChanges(ct);
        changeSignal?.Notify(ChangeDomain.WorkQueue);

        logger.LogInformation(
            "Queued manifest {ExternalId} for execution (WorkQueueId: {WorkQueueId})",
            externalId,
            entry.Id
        );
    }

    /// <inheritdoc />
    public async Task TriggerAsync(
        string externalId,
        TimeSpan delay,
        CancellationToken ct = default
    )
    {
        await using var context = CreateContext();

        var manifest = await GetManifestByExternalIdAsync(context, externalId, ct);

        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = manifest.Name,
                Input = manifest.Properties,
                InputTypeName = manifest.PropertyTypeName,
                ManifestId = manifest.Id,
                Priority = manifest.Priority,
                ScheduledAt = DateTime.UtcNow + delay,
            }
        );
        context.WorkQueues.Add(entry);
        await context.SaveChanges(ct);
        changeSignal?.Notify(ChangeDomain.WorkQueue);

        logger.LogInformation(
            "Queued delayed manifest {ExternalId} for execution at {ScheduledAt} (WorkQueueId: {WorkQueueId})",
            externalId,
            entry.ScheduledAt,
            entry.Id
        );
    }

    /// <inheritdoc />
    public Task<Manifest> ScheduleOnceAsync<TTrain, TInput, TOutput>(
        TInput input,
        TimeSpan delay,
        Action<ScheduleOptions>? options = null,
        CancellationToken ct = default
    )
        where TTrain : IServiceTrain<TInput, TOutput>
        where TInput : IManifestProperties
    {
        var externalId = $"once-{Guid.NewGuid():N}";
        return ScheduleOnceAsync<TTrain, TInput, TOutput>(externalId, input, delay, options, ct);
    }

    /// <inheritdoc />
    public async Task<Manifest> ScheduleOnceAsync<TTrain, TInput, TOutput>(
        string externalId,
        TInput input,
        TimeSpan delay,
        Action<ScheduleOptions>? options = null,
        CancellationToken ct = default
    )
        where TTrain : IServiceTrain<TInput, TOutput>
        where TInput : IManifestProperties
    {
        trainRegistry.ValidateTrainRegistration<TInput>();

        var resolved = ResolveOptions(options);

        await using var context = CreateContext();

        var manifest = await context.UpsertOnceManifestAsync<TTrain, TInput, TOutput>(
            externalId,
            input,
            DateTime.UtcNow + delay,
            resolved.ManifestOptions,
            groupId: resolved.GroupId ?? externalId,
            groupPriority: resolved.GroupPriority,
            groupMaxActiveJobs: resolved.GroupMaxActiveJobs,
            groupIsEnabled: resolved.GroupEnabled,
            ct: ct
        );

        await context.SaveChanges(ct);

        logger.LogInformation(
            "Scheduled one-off train {Train} with ExternalId {ExternalId}, fires at {ScheduledAt}",
            typeof(TTrain).Name,
            externalId,
            manifest.ScheduledAt
        );

        return manifest;
    }

    /// <inheritdoc />
    public async Task<int> TriggerGroupAsync(long groupId, CancellationToken ct = default)
    {
        await using var context = CreateContext();

        var manifests = await context
            .Manifests.AsNoTracking()
            .Where(m =>
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
                    TrainName = manifest.Name,
                    Input = manifest.Properties,
                    InputTypeName = manifest.PropertyTypeName,
                    ManifestId = manifest.Id,
                    Priority = manifest.Priority,
                }
            );
            context.WorkQueues.Add(entry);
        }

        await context.SaveChanges(ct);
        changeSignal?.Notify(ChangeDomain.WorkQueue);

        logger.LogInformation(
            "Queued {Count} manifests in group {GroupId} for execution",
            manifests.Count,
            groupId
        );

        return manifests.Count;
    }

    /// <inheritdoc />
    public async Task<int> CancelAsync(string externalId, CancellationToken ct = default)
    {
        await using var context = CreateContext();

        var manifest = await GetManifestByExternalIdAsync(context, externalId, ct);

        var flagged = await ExecutionCancellation.RequestAsync(
            context,
            context.Metadatas.Where(m => m.ManifestId == manifest.Id),
            cancellationRegistry,
            changeSignal,
            ct
        );

        if (flagged > 0)
            logger.LogInformation(
                "Cancellation requested for {Count} pending or in-progress execution(s) of manifest {ExternalId}",
                flagged,
                externalId
            );

        return flagged;
    }

    /// <inheritdoc />
    public async Task<int> CancelGroupAsync(long groupId, CancellationToken ct = default)
    {
        await using var context = CreateContext();

        var flagged = await ExecutionCancellation.RequestAsync(
            context,
            context.Metadatas.Where(m =>
                m.Manifest != null && m.Manifest.ManifestGroupId == groupId
            ),
            cancellationRegistry,
            changeSignal,
            ct
        );

        if (flagged > 0)
            logger.LogInformation(
                "Cancellation requested for {Count} pending or in-progress execution(s) in group {GroupId}",
                flagged,
                groupId
            );

        return flagged;
    }

    // ── Internal non-generic overloads (used by TrainConfigurator) ───

    internal async Task<Manifest> ScheduleAsyncUntyped(
        Type trainType,
        Type inputType,
        string externalId,
        IManifestProperties input,
        Schedule schedule,
        Action<ScheduleOptions>? options = null,
        CancellationToken ct = default
    )
    {
        trainRegistry.ValidateTrainRegistration(inputType);

        var resolved = ResolveOptions(options);

        await using var context = CreateContext();

        var manifest = await context.UpsertManifestAsync(
            trainType,
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
            "Scheduled train {Train} with ExternalId {ExternalId}",
            trainType.Name,
            externalId
        );

        return manifest;
    }

    internal async Task<Manifest> ScheduleOnceAsyncUntyped(
        Type trainType,
        Type inputType,
        string externalId,
        IManifestProperties input,
        TimeSpan delay,
        Action<ScheduleOptions>? options = null,
        CancellationToken ct = default
    )
    {
        trainRegistry.ValidateTrainRegistration(inputType);

        var resolved = ResolveOptions(options);

        await using var context = CreateContext();

        var manifest = await context.UpsertOnceManifestAsync(
            trainType,
            externalId,
            input,
            DateTime.UtcNow + delay,
            resolved.ManifestOptions,
            groupId: resolved.GroupId ?? externalId,
            groupPriority: resolved.GroupPriority,
            groupMaxActiveJobs: resolved.GroupMaxActiveJobs,
            groupIsEnabled: resolved.GroupEnabled,
            ct: ct
        );

        await context.SaveChanges(ct);

        logger.LogInformation(
            "Scheduled one-off train {Train} with ExternalId {ExternalId}, fires at {ScheduledAt}",
            trainType.Name,
            externalId,
            manifest.ScheduledAt
        );

        return manifest;
    }

    internal async Task<Manifest> ScheduleDependentAsyncUntyped(
        Type trainType,
        Type inputType,
        string externalId,
        IManifestProperties input,
        string dependsOnExternalId,
        Action<ScheduleOptions>? options = null,
        CancellationToken ct = default
    )
    {
        trainRegistry.ValidateTrainRegistration(inputType);

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
            trainType,
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
            "Scheduled dependent train {Train} with ExternalId {ExternalId} depending on {ParentExternalId}",
            trainType.Name,
            externalId,
            dependsOnExternalId
        );

        return manifest;
    }

    internal async Task<IReadOnlyList<Manifest>> ScheduleManyAsyncUntyped<TSource>(
        Type trainType,
        Type inputType,
        IEnumerable<TSource> sources,
        Func<TSource, (string ExternalId, IManifestProperties Input)> map,
        Schedule schedule,
        Action<ScheduleOptions>? options = null,
        Action<TSource, ManifestOptions>? configureEach = null,
        CancellationToken ct = default
    )
    {
        trainRegistry.ValidateTrainRegistration(inputType);

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
                    trainType,
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
            await context.CommitTransaction();

            logger.LogInformation(
                "Scheduled {Count} manifests for train {Train} in single transaction",
                results.Count,
                trainType.Name
            );

            if (resolved.PrunePrefix is not null)
            {
                var keepIds = results.Select(m => m.ExternalId).ToHashSet();
                await PruneSafeAsync(resolved.PrunePrefix, keepIds, ct);
            }

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
        Type trainType,
        Type inputType,
        IEnumerable<TSource> sources,
        Func<TSource, (string ExternalId, IManifestProperties Input)> map,
        Func<TSource, string> dependsOn,
        Action<ScheduleOptions>? options = null,
        Action<TSource, ManifestOptions>? configureEach = null,
        CancellationToken ct = default
    )
    {
        trainRegistry.ValidateTrainRegistration(inputType);

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
                    trainType,
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
            await context.CommitTransaction();

            logger.LogInformation(
                "Scheduled {Count} dependent manifests for train {Train} in single transaction",
                results.Count,
                trainType.Name
            );

            if (resolved.PrunePrefix is not null)
            {
                var keepIds = results.Select(m => m.ExternalId).ToHashSet();
                await PruneSafeAsync(resolved.PrunePrefix, keepIds, ct);
            }

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

    // ── Dead Letter Operations ────────────────────────────────────────────

    /// <summary>
    /// Test seam: awaited after a requeue has checked for a queued entry and before it inserts,
    /// on the first attempt only, so a test can hold two requeues inside that window at once.
    /// </summary>
    internal Func<CancellationToken, Task>? BeforeRequeueInsert { get; set; }

    /// <inheritdoc />
    public async Task<DeadLetterOperationResult> RequeueDeadLetterAsync(
        long deadLetterId,
        CancellationToken ct = default
    )
    {
        await using var context = CreateContext();

        var deadLetter = await context
            .DeadLetters.Include(d => d.Manifest)
            .FirstOrDefaultAsync(
                d => d.Id == deadLetterId && d.Status == DeadLetterStatus.AwaitingIntervention,
                ct
            );

        if (deadLetter is null)
            return new DeadLetterOperationResult(
                false,
                null,
                "Dead letter not found or already resolved"
            );

        // One queued entry per manifest (ix_work_queue_unique_queued_manifest). A second would
        // fail the insert; the queued one already runs the manifest's work.
        var queuedId = await context
            .WorkQueues.Where(q =>
                q.ManifestId == deadLetter.ManifestId && q.Status == WorkQueueStatus.Queued
            )
            .Select(q => (long?)q.Id)
            .FirstOrDefaultAsync(ct);

        if (queuedId is { } existing)
            return new DeadLetterOperationResult(
                false,
                null,
                $"The manifest already has a queued entry (WorkQueue {existing}); the dead letter "
                    + "is left awaiting intervention."
            );

        var entry = CreateWorkQueueFromDeadLetter(deadLetter);
        context.WorkQueues.Add(entry);

        deadLetter.Requeue($"Re-queued (WorkQueue {entry.Id})");

        try
        {
            await context.SaveChanges(ct);
        }
        catch (DbUpdateException)
        {
            // Queued by someone else between the check above and this insert: the index refused
            // the second entry and nothing was written. Anything else is rethrown.
            if (!await AnyQueuedAsync([deadLetter.ManifestId], ct))
                throw;

            return new DeadLetterOperationResult(
                false,
                null,
                "The manifest already has a queued entry; the dead letter is left awaiting "
                    + "intervention."
            );
        }

        // WorkQueue ID is available after SaveChanges — update the resolution note
        deadLetter.ResolutionNote = $"Re-queued (WorkQueue {entry.Id})";
        await context.SaveChanges(ct);

        // A requeue both resolves a dead letter and creates a work-queue entry.
        changeSignal?.Notify(ChangeDomain.DeadLetter);
        changeSignal?.Notify(ChangeDomain.WorkQueue);

        logger.LogInformation(
            "Requeued dead letter {DeadLetterId} as WorkQueue {WorkQueueId}",
            deadLetterId,
            entry.Id
        );

        return new DeadLetterOperationResult(true, entry.Id, "Dead letter requeued");
    }

    /// <inheritdoc />
    public async Task<DeadLetterOperationResult> AcknowledgeDeadLetterAsync(
        long deadLetterId,
        string note,
        CancellationToken ct = default
    )
    {
        await using var context = CreateContext();

        var deadLetter = await context.DeadLetters.FirstOrDefaultAsync(
            d => d.Id == deadLetterId && d.Status == DeadLetterStatus.AwaitingIntervention,
            ct
        );

        if (deadLetter is null)
            return new DeadLetterOperationResult(
                false,
                null,
                "Dead letter not found or already resolved"
            );

        deadLetter.Acknowledge(note);
        await context.SaveChanges(ct);
        changeSignal?.Notify(ChangeDomain.DeadLetter);

        logger.LogInformation("Acknowledged dead letter {DeadLetterId}", deadLetterId);

        return new DeadLetterOperationResult(true, null, "Dead letter acknowledged");
    }

    /// <inheritdoc />
    public async Task<BatchDeadLetterResult> RequeueDeadLettersAsync(
        long[] deadLetterIds,
        CancellationToken ct = default
    )
    {
        return await RequeueDeadLetterBatch(
            context =>
                context.DeadLetters.Where(d =>
                    deadLetterIds.Contains(d.Id)
                    && d.Status == DeadLetterStatus.AwaitingIntervention
                ),
            ct
        );
    }

    /// <inheritdoc />
    public async Task<BatchDeadLetterResult> AcknowledgeDeadLettersAsync(
        long[] deadLetterIds,
        string note,
        CancellationToken ct = default
    )
    {
        await using var context = CreateContext();

        var acknowledged = await AcknowledgeAwaitingAsync(
            context,
            context.DeadLetters.Where(d =>
                deadLetterIds.Contains(d.Id) && d.Status == DeadLetterStatus.AwaitingIntervention
            ),
            note,
            ct
        );

        logger.LogInformation("Acknowledged {Count} dead letters", acknowledged);

        return new BatchDeadLetterResult(
            acknowledged,
            $"{acknowledged} dead letter(s) acknowledged"
        );
    }

    /// <inheritdoc />
    public async Task<BatchDeadLetterResult> RequeueAllDeadLettersAsync(
        CancellationToken ct = default
    )
    {
        return await RequeueDeadLetterBatch(
            context =>
                context.DeadLetters.Where(d => d.Status == DeadLetterStatus.AwaitingIntervention),
            ct
        );
    }

    /// <inheritdoc />
    public async Task<BatchDeadLetterResult> AcknowledgeAllDeadLettersAsync(
        string note,
        CancellationToken ct = default
    )
    {
        await using var context = CreateContext();

        var acknowledged = await AcknowledgeAwaitingAsync(
            context,
            context.DeadLetters.Where(d => d.Status == DeadLetterStatus.AwaitingIntervention),
            note,
            ct
        );

        logger.LogInformation("Acknowledged all {Count} dead letters", acknowledged);

        return new BatchDeadLetterResult(
            acknowledged,
            $"{acknowledged} dead letter(s) acknowledged"
        );
    }

    /// <summary>
    /// Acknowledges every dead letter <paramref name="awaiting"/> selects, in one statement on a
    /// relational store: loading and saving each row took seconds per fifty thousand, which an
    /// "acknowledge all" over a large backlog reaches. The InMemory provider has no set-based
    /// update, so there the rows are loaded and saved, which is what every provider used to do.
    /// Signals <c>DeadLetter</c> when any row changed.
    /// </summary>
    private async Task<int> AcknowledgeAwaitingAsync(
        IDataContext context,
        IQueryable<Effect.Models.DeadLetter.DeadLetter> awaiting,
        string note,
        CancellationToken ct
    )
    {
        int acknowledged;

        if (((DbContext)context).Database.IsRelational())
        {
            var now = DateTime.UtcNow;
            acknowledged = await awaiting.ExecuteUpdateAsync(
                s =>
                    s.SetProperty(d => d.Status, DeadLetterStatus.Acknowledged)
                        .SetProperty(d => d.ResolvedAt, now)
                        .SetProperty(d => d.ResolutionNote, note),
                ct
            );
        }
        else
        {
            var rows = await awaiting.ToListAsync(ct);
            foreach (var row in rows)
                row.Acknowledge(note);
            await context.SaveChanges(ct);
            acknowledged = rows.Count;
        }

        if (acknowledged > 0)
            changeSignal?.Notify(ChangeDomain.DeadLetter);

        return acknowledged;
    }

    /// <summary>
    /// Requeues a batch of dead letters with at most one queued entry per manifest, which is what
    /// <c>ix_work_queue_unique_queued_manifest</c> allows. A dead letter whose manifest already
    /// has a queued entry is skipped and stays awaiting intervention. Dead letters that share a
    /// manifest are folded into one entry: a requeue runs the manifest's own properties, so each
    /// would queue the same work. The newest one carries the entry's <c>DeadLetterId</c>, and every
    /// one of them is resolved with a note naming the entry. The result's message counts both.
    /// </summary>
    /// <remarks>
    /// The "already queued?" check and the insert are separate statements, so a concurrent
    /// requeue or the ManifestManager can queue one of the manifests in between, and the insert
    /// then fails on the index. That is caught provider-neutrally: when the save fails and one of
    /// the manifests this attempt meant to queue now has a queued entry, the whole attempt is
    /// rolled back and rerun from a fresh read, which skips that manifest and no longer sees dead
    /// letters another requeue resolved. Any other failure is rethrown.
    /// </remarks>
    private async Task<BatchDeadLetterResult> RequeueDeadLetterBatch(
        Func<IDataContext, IQueryable<Effect.Models.DeadLetter.DeadLetter>> select,
        CancellationToken ct
    )
    {
        const int maxAttempts = 3;

        for (var attempt = 1; ; attempt++)
        {
            await using var context = CreateContext();
            var deadLetters = await select(context).Include(d => d.Manifest).ToListAsync(ct);

            try
            {
                return await RequeueDeadLetterBatchOnce(context, deadLetters, attempt == 1, ct);
            }
            catch (DbUpdateException ex) when (attempt < maxAttempts)
            {
                var manifestIds = deadLetters.Select(d => d.ManifestId).Distinct().ToList();
                if (!await AnyQueuedAsync(manifestIds, ct))
                    throw;

                logger.LogInformation(
                    ex,
                    "A dead-letter requeue lost a race for a manifest's queued entry; retrying (attempt {Attempt})",
                    attempt
                );
            }
        }
    }

    private async Task<bool> AnyQueuedAsync(List<long> manifestIds, CancellationToken ct)
    {
        await using var context = CreateContext();
        return await context.WorkQueues.AnyAsync(
            q =>
                q.ManifestId != null
                && manifestIds.Contains(q.ManifestId.Value)
                && q.Status == WorkQueueStatus.Queued,
            ct
        );
    }

    private async Task<BatchDeadLetterResult> RequeueDeadLetterBatchOnce(
        IDataContext context,
        List<Effect.Models.DeadLetter.DeadLetter> deadLetters,
        bool firstAttempt,
        CancellationToken ct
    )
    {
        var manifestIds = deadLetters.Select(d => d.ManifestId).Distinct().ToList();
        var alreadyQueued = (
            await context
                .WorkQueues.Where(q =>
                    q.ManifestId != null
                    && manifestIds.Contains(q.ManifestId.Value)
                    && q.Status == WorkQueueStatus.Queued
                )
                .Select(q => q.ManifestId!.Value)
                .ToListAsync(ct)
        ).ToHashSet();

        var skipped = deadLetters.Count(d => alreadyQueued.Contains(d.ManifestId));
        var batches = deadLetters
            .Where(d => !alreadyQueued.Contains(d.ManifestId))
            .GroupBy(d => d.ManifestId)
            .Select(g =>
            {
                var members = g.OrderByDescending(d => d.Id).ToList();
                return (Entry: CreateWorkQueueFromDeadLetter(members[0]), Members: members);
            })
            .ToList();

        if (firstAttempt && BeforeRequeueInsert is { } hook)
            await hook(ct);

        foreach (var (entry, members) in batches)
        {
            context.WorkQueues.Add(entry);
            foreach (var dl in members)
                dl.Requeue("Re-queued (batch)");
        }

        await context.SaveChanges(ct);

        // Entry ids exist only after the first save; name them in the notes, as a single requeue does.
        foreach (var (entry, members) in batches)
        {
            members[0].ResolutionNote = $"Re-queued (WorkQueue {entry.Id})";
            foreach (var dl in members.Skip(1))
                dl.ResolutionNote =
                    $"Re-queued with dead letter {members[0].Id} (WorkQueue {entry.Id})";
        }

        var resolved = batches.Sum(b => b.Members.Count);
        var folded = resolved - batches.Count;

        if (resolved > 0)
        {
            await context.SaveChanges(ct);
            changeSignal?.Notify(ChangeDomain.DeadLetter);
            changeSignal?.Notify(ChangeDomain.WorkQueue);
        }

        logger.LogInformation(
            "Requeued {Count} dead letters as {Entries} work queue entries ({Folded} folded, {Skipped} skipped)",
            resolved,
            batches.Count,
            folded,
            skipped
        );

        var message = $"{resolved} dead letter(s) requeued";
        if (folded > 0)
            message += $"; {folded} folded into another dead letter's entry for the same manifest";
        if (skipped > 0)
            message += $"; {skipped} skipped because their manifest already has a queued entry";

        return new BatchDeadLetterResult(resolved, message + ".");
    }

    private static WorkQueue CreateWorkQueueFromDeadLetter(
        Effect.Models.DeadLetter.DeadLetter deadLetter
    )
    {
        var manifest = deadLetter.Manifest!;
        return WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = manifest.Name,
                Input = manifest.Properties,
                InputTypeName = manifest.PropertyTypeName,
                ManifestId = manifest.Id,
                Priority = manifest.Priority,
                DeadLetterId = deadLetter.Id,
            }
        );
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
            Exclusions = baseOptions.Exclusions,
            Variance = baseOptions.Variance,
        };

    private static async Task<Manifest> GetManifestByExternalIdAsync(
        IDataContext context,
        string externalId,
        CancellationToken ct
    ) =>
        await context.Manifests.FirstOrDefaultAsync(m => m.ExternalId == externalId, ct)
        ?? throw new InvalidOperationException($"No manifest found with ExternalId '{externalId}'");

    private async Task PruneSafeAsync(
        string prunePrefix,
        System.Collections.Generic.HashSet<string> keepExternalIds,
        CancellationToken ct
    )
    {
        try
        {
            await using var pruneContext = CreateContext();
            await PruneStaleManifestsAsync(pruneContext, prunePrefix, keepExternalIds, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Failed to prune stale manifests with prefix '{Prefix}' — will retry on next startup",
                prunePrefix
            );
        }
    }

    private async Task PruneStaleManifestsAsync(
        IDataContext context,
        string prunePrefix,
        System.Collections.Generic.HashSet<string> keepExternalIds,
        CancellationToken ct
    )
    {
        // Server compute: load prefixed manifest IDs, filter stale ones in C#.
        // Avoids a NOT IN(...) clause with many string parameters that can timeout
        // on low-resource Postgres instances during query planning.
        var prefixedManifests = await context
            .Manifests.Where(m => m.ExternalId.StartsWith(prunePrefix))
            .Select(m => new { m.Id, m.ExternalId })
            .ToListAsync(ct);

        var staleManifestIds = prefixedManifests
            .Where(m => !keepExternalIds.Contains(m.ExternalId))
            .Select(m => m.Id)
            .ToList();

        if (staleManifestIds.Count == 0)
            return;

        // Database compute: delete by integer PK — small IN(...) clause per query.
        await context
            .WorkQueues.Where(w =>
                w.ManifestId.HasValue && staleManifestIds.Contains(w.ManifestId.Value)
            )
            .ExecuteDeleteAsync(ct);

        await context
            .DeadLetters.Where(d => staleManifestIds.Contains(d.ManifestId))
            .ExecuteDeleteAsync(ct);

        await context
            .Metadatas.Where(m =>
                m.ManifestId.HasValue && staleManifestIds.Contains(m.ManifestId.Value)
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
