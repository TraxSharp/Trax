using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Trax.Core.Functional;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Enums;
using Trax.Effect.Models.Manifest;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Services.ChangeSignal;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainRegistry;
using Trax.Scheduler.Configuration;
using Trax.Scheduler.Extensions;
using Trax.Scheduler.Services.CancellationRegistry;
using Trax.Scheduler.Services.ManifestPruning;
using Trax.Scheduler.Services.Operations;
using Trax.Scheduler.Trains.ManifestManager.Utilities;
using Schedule = Trax.Scheduler.Services.Scheduling.Schedule;

namespace Trax.Scheduler.Services.TraxScheduler;

/// <summary>
/// Implementation of <see cref="ITraxScheduler"/> that provides type-safe manifest scheduling.
/// </summary>
/// <param name="dataContextFactory">Creates the data context each operation uses.</param>
/// <param name="trainRegistry">Validates that a scheduled train is registered.</param>
/// <param name="trainDiscovery">
/// Lets scheduling check that the train itself is registered, not only a train taking its input
/// type: a scheduled run runs the train it names. Null checks the input type only.
/// </param>
/// <param name="cancellationRegistry">Cancels runs in this process.</param>
/// <param name="logger">The scheduler's logger.</param>
/// <param name="configuration">
/// The scheduler configuration, whose <c>DefaultMaxRetries</c> and <c>DefaultMisfirePolicy</c> a
/// manifest takes when its options state neither. Null keeps the built-in defaults.
/// </param>
/// <param name="changeSignal">Optional; always resolved via DI in a host.</param>
/// <remarks>
/// This is the constructor dependency injection uses: it takes every other constructor's
/// parameters and more, so the container always picks it.
/// </remarks>
public class TraxScheduler(
    IDataContextProviderFactory dataContextFactory,
    ITrainRegistry trainRegistry,
    ITrainDiscoveryService? trainDiscovery,
    ICancellationRegistry cancellationRegistry,
    ILogger<TraxScheduler> logger,
    SchedulerConfiguration? configuration,
    ITraxChangeSignal? changeSignal = null
) : ITraxScheduler
{
    /// <summary>
    /// The longest acknowledgement note accepted, in characters. A note is an operator's reason,
    /// a sentence or a paragraph; <see cref="AcknowledgeAllDeadLettersAsync"/> writes it onto every
    /// awaiting row, so an unbounded one multiplies into the table by the size of the backlog. A
    /// longer note is refused, not cut, so what is stored is what the operator wrote. The
    /// dashboard and the GraphQL API both acknowledge through this scheduler, so both refuse it.
    /// </summary>
    public const int MaxAcknowledgeNoteLength = 1_000;

    /// <summary>Why a note is refused, or null when it is accepted.</summary>
    internal static string? NoteRefusal(string? note) =>
        note is { Length: > MaxAcknowledgeNoteLength }
            ? $"The note is {note.Length} characters; it may be at most {MaxAcknowledgeNoteLength} characters."
            : null;

    /// <summary>
    /// The constructor as it shipped before the discovery service and configuration parameters,
    /// kept so code built against it still binds. A scheduler built this way checks only the
    /// input type when scheduling, and applies the built-in defaults rather than the configured
    /// <c>DefaultMaxRetries</c> and <c>DefaultMisfirePolicy</c>.
    /// </summary>
    public TraxScheduler(
        IDataContextProviderFactory dataContextFactory,
        ITrainRegistry trainRegistry,
        ICancellationRegistry cancellationRegistry,
        ILogger<TraxScheduler> logger,
        ITraxChangeSignal? changeSignal = null
    )
        : this(
            dataContextFactory,
            trainRegistry,
            trainDiscovery: null,
            cancellationRegistry,
            logger,
            configuration: null,
            changeSignal
        ) { }

    /// <summary>
    /// Records this application as the manifest's owner, so only this application's startup prune
    /// ever considers it. Leaves the owner alone when the application has no name.
    /// </summary>
    private void StampOwner(Manifest manifest)
    {
        if (configuration?.Owner is { } owner)
            manifest.Owner = owner;
    }

    private void ValidateTrain(Type trainType, Type inputType) =>
        trainRegistry.ValidateTrainRegistration(trainDiscovery, trainType, inputType);

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
        ValidateTrain(typeof(TTrain), typeof(TInput));

        var resolved = ResolveOptions(options);

        await using var context = CreateContext();

        var manifest = await context.UpsertManifestAsync<TTrain, TInput, TOutput>(
            externalId,
            input,
            schedule,
            resolved.ManifestOptions,
            groupId: resolved.GroupId ?? externalId,
            group: resolved.Group,
            ct: ct
        );

        StampOwner(manifest);

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
        ValidateTrain(typeof(TTrain), typeof(TInput));

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
                var itemOptions = resolved.ManifestOptions.Copy();
                configureEach?.Invoke(source, itemOptions);

                var manifest = await context.UpsertManifestAsync<TTrain, TInput, TOutput>(
                    externalId,
                    input,
                    schedule,
                    itemOptions,
                    groupId: effectiveGroupId,
                    group: resolved.Group,
                    ct: ct
                );

                StampOwner(manifest);
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
                await PruneSafeAsync(
                    resolved.PrunePrefix,
                    resolved.BatchName is null ? null : effectiveGroupId,
                    keepIds,
                    ct
                );
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
        ValidateTrain(typeof(TTrain), typeof(TInput));

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
            group: resolved.Group,
            ct: ct
        );

        StampOwner(manifest);

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
        ValidateTrain(typeof(TTrain), typeof(TInput));

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

                var itemOptions = resolved.ManifestOptions.Copy();
                configureEach?.Invoke(source, itemOptions);

                var manifest = await context.UpsertDependentManifestAsync<TTrain, TInput, TOutput>(
                    externalId,
                    input,
                    parentManifest.Id,
                    itemOptions,
                    groupId: effectiveGroupId,
                    group: resolved.Group,
                    ct: ct
                );

                StampOwner(manifest);
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
                await PruneSafeAsync(
                    resolved.PrunePrefix,
                    resolved.BatchName is null ? null : effectiveGroupId,
                    keepIds,
                    ct
                );
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
    public Task TriggerAsync(string externalId, CancellationToken ct = default) =>
        TriggerAsync(externalId, askAfresh: false, ct);

    /// <inheritdoc />
    public async Task<ManifestTriggerResult> TriggerAsync(
        string externalId,
        bool askAfresh,
        CancellationToken ct = default
    )
    {
        await using var context = CreateContext();

        var manifest = await GetManifestByExternalIdAsync(context, externalId, ct);

        var outcome = await TriggerManifestAsync(
            context,
            manifest,
            runAt: DateTime.UtcNow,
            askAfresh,
            ct
        );
        changeSignal?.Notify(ChangeDomain.WorkQueue);
        LogTrigger(externalId, outcome, askAfresh);
        return outcome.ToResult();
    }

    /// <inheritdoc />
    public Task TriggerAsync(string externalId, TimeSpan delay, CancellationToken ct = default) =>
        TriggerAsync(externalId, delay, askAfresh: false, ct);

    /// <inheritdoc />
    public async Task<ManifestTriggerResult> TriggerAsync(
        string externalId,
        TimeSpan delay,
        bool askAfresh,
        CancellationToken ct = default
    )
    {
        await using var context = CreateContext();

        var manifest = await GetManifestByExternalIdAsync(context, externalId, ct);

        var outcome = await TriggerManifestAsync(
            context,
            manifest,
            DateTime.UtcNow + delay,
            askAfresh,
            ct
        );
        changeSignal?.Notify(ChangeDomain.WorkQueue);
        LogTrigger(externalId, outcome, askAfresh);
        return outcome.ToResult();
    }

    private void LogTrigger(string externalId, TriggerOutcome outcome, bool askAfresh)
    {
        if (outcome.AlreadyDispatched)
        {
            if (askAfresh && outcome.ReplayDecisionsOf is { } replayed)
                logger.LogWarning(
                    "Manifest {ExternalId}'s queued entry (WorkQueueId: {WorkQueueId}) was dispatched "
                        + "before the trigger reached it, so it was not asked afresh: its run replays "
                        + "the decisions of run {ReplayDecisionsOf}",
                    externalId,
                    outcome.WorkQueueId,
                    replayed
                );
            else
                logger.LogInformation(
                    "Manifest {ExternalId}'s queued entry (WorkQueueId: {WorkQueueId}) was dispatched "
                        + "before the trigger reached it; the trigger queued nothing more",
                    externalId,
                    outcome.WorkQueueId
                );
        }
        else if (outcome.Created)
            logger.LogInformation(
                "Queued manifest {ExternalId} for execution at {ScheduledAt} (WorkQueueId: {WorkQueueId})",
                externalId,
                outcome.ScheduledAt,
                outcome.WorkQueueId
            );
        else if (outcome.MovedForward)
            logger.LogInformation(
                "Manifest {ExternalId} already had a queued entry (WorkQueueId: {WorkQueueId}) due later; the trigger moved it forward to {ScheduledAt} and queued nothing more",
                externalId,
                outcome.WorkQueueId,
                outcome.ScheduledAt
            );
        else
            logger.LogInformation(
                "Manifest {ExternalId} already has a queued entry (WorkQueueId: {WorkQueueId}) due at {ScheduledAt}; the trigger released it and queued nothing more",
                externalId,
                outcome.WorkQueueId,
                outcome.ScheduledAt
            );
    }

    /// <summary>
    /// What a trigger did: queued a new entry, or found the manifest's queued one and, when that
    /// entry was due later than the trigger asked, moved it forward. <see cref="ScheduledAt"/> is
    /// when the entry is now due; null means immediately.
    /// </summary>
    internal readonly record struct TriggerOutcome(
        long WorkQueueId,
        bool Created,
        bool MovedForward,
        DateTime? ScheduledAt,
        bool AlreadyDispatched = false,
        long? ReplayDecisionsOf = null
    )
    {
        public ManifestTriggerResult ToResult() =>
            new(WorkQueueId, Created, ScheduledAt, AlreadyDispatched, ReplayDecisionsOf)
            {
                MovedForward = MovedForward,
            };
    }

    /// <summary>
    /// Queues one entry for <paramref name="manifest"/>, due at <paramref name="runAt"/> and
    /// marked as asked for by name (<see cref="WorkQueue.IsExplicitTrigger"/>) so it runs even
    /// while the manifest is disabled. The database holds at most one queued entry per manifest
    /// (<c>ix_work_queue_unique_queued_manifest</c>), so when one is already there nothing more is
    /// queued and that entry becomes the triggered run instead: it is marked the same way, and an
    /// entry due later than <paramref name="runAt"/> (a retry waiting out its backoff, say) is
    /// brought forward to it. The check and the insert are two statements, so an entry the
    /// ManifestManager or another trigger queues between them is recognised by the insert's
    /// failure and treated the same way; any other failed save is thrown.
    /// </summary>
    private Task<TriggerOutcome> TriggerManifestAsync(
        IDataContext context,
        Manifest manifest,
        DateTime runAt,
        bool askAfresh,
        CancellationToken ct
    ) => TriggerManifestAsync(context, manifest, runAt, askAfresh, BeforeTriggerRelease, ct);

    /// <summary>
    /// The trigger itself, for <see cref="TriggerManifestAsync(IDataContext, Manifest, DateTime, bool, CancellationToken)"/>
    /// and for the operations service's batch trigger, so a manifest triggered from a list is
    /// triggered exactly as one triggered by name. It stays in this file because this file is
    /// where a manifest's trigger may build a work queue row (docs/0017).
    /// </summary>
    internal static async Task<TriggerOutcome> TriggerManifestAsync(
        IDataContext context,
        Manifest manifest,
        DateTime runAt,
        bool askAfresh,
        Func<long, CancellationToken, Task>? beforeRelease,
        CancellationToken ct
    )
    {
        if (
            await ReleaseQueuedEntryAsync(
                context,
                manifest.Id,
                runAt,
                askAfresh,
                beforeRelease,
                ct
            ) is
            { } queued
        )
            return queued;

        // An immediate trigger stores no time, as it always has; a delayed one stores its time.
        DateTime? scheduledAt = runAt > DateTime.UtcNow ? runAt : null;
        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = manifest.Name,
                Input = manifest.Properties,
                InputTypeName = manifest.PropertyTypeName,
                ManifestId = manifest.Id,
                Priority = manifest.Priority,
                ScheduledAt = scheduledAt,
                ExplicitTrigger = true,
            }
        );
        context.WorkQueues.Add(entry);

        try
        {
            await context.SaveChanges(ct);
            return new TriggerOutcome(entry.Id, Created: true, MovedForward: false, scheduledAt);
        }
        catch (DbUpdateException)
        {
            // Untracked either way, so a later save on this context does not retry it.
            context.Reset();

            if (
                await ReleaseQueuedEntryAsync(
                    context,
                    manifest.Id,
                    runAt,
                    askAfresh,
                    beforeRelease,
                    ct
                ) is
                { } raced
            )
                return raced;

            throw;
        }
    }

    /// <summary>
    /// Test seam: awaited with the entry's id between a trigger reading the manifest's queued entry
    /// and releasing it.
    /// </summary>
    internal Func<long, CancellationToken, Task>? BeforeTriggerRelease { get; set; }

    /// <summary>
    /// Makes the manifest's queued entry, if it has one, the triggered run: marks it as asked for
    /// by name and, when it is due later than <paramref name="runAt"/>, brings it forward to that
    /// time. Null when the manifest has no queued entry.
    /// </summary>
    /// <remarks>
    /// The update is conditional on the entry still being queued, so an entry the dispatcher claims
    /// in the meantime is left as it is: it is already running, which is what the trigger asked
    /// for. The outcome reports it as already dispatched, with the link its run kept, because a
    /// trigger that asked afresh did not get what it asked for.
    /// </remarks>
    private static async Task<TriggerOutcome?> ReleaseQueuedEntryAsync(
        IDataContext context,
        long manifestId,
        DateTime runAt,
        bool askAfresh,
        Func<long, CancellationToken, Task>? beforeRelease,
        CancellationToken ct
    )
    {
        var queued = await context
            .WorkQueues.AsNoTracking()
            .Where(w => w.ManifestId == manifestId && w.Status == WorkQueueStatus.Queued)
            .Select(w => new
            {
                w.Id,
                w.ScheduledAt,
                w.ReplayDecisionsOf,
            })
            .FirstOrDefaultAsync(ct);

        if (queued is null)
            return null;

        if (beforeRelease is not null)
            await beforeRelease(queued.Id, ct);

        var moveForward = queued.ScheduledAt > runAt;
        // Brought forward to now, the entry stores no time, as an immediate trigger's new entry
        // does: the dispatcher compares a stored time with the database's clock, and this host's
        // "now" may still be ahead of it.
        var dueAt = moveForward
            ? (runAt > DateTime.UtcNow ? runAt : (DateTime?)null)
            : queued.ScheduledAt;

        var entry = context.WorkQueues.Where(w =>
            w.Id == queued.Id && w.Status == WorkQueueStatus.Queued
        );
        int released;
        if (context.SupportsSetUpdates())
            released = await entry.ExecuteUpdateAsync(
                s =>
                    s.SetProperty(w => w.IsExplicitTrigger, true)
                        .SetProperty(w => w.ScheduledAt, dueAt)
                        // Asked to ask afresh, a queued retry no longer replays its failed run.
                        .SetProperty(
                            w => w.ReplayDecisionsOf,
                            w => askAfresh ? (long?)null : w.ReplayDecisionsOf
                        ),
                ct
            );
        else
            released = await context.UpdateEachAsync(
                entry,
                w =>
                {
                    w.IsExplicitTrigger = true;
                    w.ScheduledAt = dueAt;
                    if (askAfresh)
                        w.ReplayDecisionsOf = null;
                },
                ct
            );

        if (released == 0)
        {
            // Claimed between the read and the update: the entry keeps its time and its link.
            var claimed = await context
                .WorkQueues.AsNoTracking()
                .Where(w => w.Id == queued.Id)
                .Select(w => new { w.ScheduledAt, w.ReplayDecisionsOf })
                .FirstOrDefaultAsync(ct);
            return new TriggerOutcome(
                queued.Id,
                Created: false,
                MovedForward: false,
                claimed?.ScheduledAt ?? queued.ScheduledAt,
                AlreadyDispatched: true,
                claimed is null ? queued.ReplayDecisionsOf : claimed.ReplayDecisionsOf
            );
        }

        return new TriggerOutcome(
            queued.Id,
            Created: false,
            moveForward,
            dueAt,
            ReplayDecisionsOf: askAfresh ? null : queued.ReplayDecisionsOf
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
        ValidateTrain(typeof(TTrain), typeof(TInput));

        var resolved = ResolveOptions(options);

        await using var context = CreateContext();

        var manifest = await context.UpsertOnceManifestAsync<TTrain, TInput, TOutput>(
            externalId,
            input,
            DateTime.UtcNow + delay,
            resolved.ManifestOptions,
            groupId: resolved.GroupId ?? externalId,
            group: resolved.Group,
            ct: ct
        );

        StampOwner(manifest);

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

        var manifests = await TriggerableInGroups(context.Manifests.AsNoTracking(), [groupId])
            .ToListAsync(ct);

        if (manifests.Count == 0)
            return 0;

        // One save per manifest, so a manifest that already has a queued entry is skipped rather
        // than failing the whole group on the unique index.
        var now = DateTime.UtcNow;
        var queued = 0;
        foreach (var manifest in manifests)
        {
            if (
                (
                    await TriggerManifestAsync(context, manifest, runAt: now, askAfresh: false, ct)
                ).Created
            )
                queued++;

            // A save checks every entity the context tracks, so one still holding the entries of
            // the triggers before makes each save slower than the last.
            context.Reset();
        }

        if (queued > 0)
            changeSignal?.Notify(ChangeDomain.WorkQueue);

        logger.LogInformation(
            "Queued {Count} manifests in group {GroupId} for execution; {Skipped} already queued",
            queued,
            groupId,
            manifests.Count - queued
        );

        return queued;
    }

    /// <summary>
    /// The members of the given groups a group trigger queues: enabled manifests that run on
    /// their own schedule. Dependent and DormantDependent manifests are left out, because they
    /// run after their parent and may lack standalone inputs. Shared with the operations
    /// service's batch group trigger, so the two choose the same manifests.
    /// </summary>
    internal static IQueryable<Manifest> TriggerableInGroups(
        IQueryable<Manifest> manifests,
        IReadOnlyCollection<long> groupIds
    ) =>
        manifests.Where(m =>
            groupIds.Contains(m.ManifestGroupId)
            && m.IsEnabled
            && m.ScheduleType != ScheduleType.Dependent
            && m.ScheduleType != ScheduleType.DormantDependent
        );

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
            await ExecutionCancellation.InGroupsAsync(context, [groupId], ct),
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
        ValidateTrain(trainType, inputType);

        var resolved = ResolveOptions(options);

        await using var context = CreateContext();

        var manifest = await context.UpsertManifestAsync(
            trainType,
            externalId,
            input,
            schedule,
            resolved.ManifestOptions,
            groupId: resolved.GroupId ?? externalId,
            group: resolved.Group,
            ct: ct
        );

        StampOwner(manifest);

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
        ValidateTrain(trainType, inputType);

        var resolved = ResolveOptions(options);

        await using var context = CreateContext();

        var manifest = await context.UpsertOnceManifestAsync(
            trainType,
            externalId,
            input,
            DateTime.UtcNow + delay,
            resolved.ManifestOptions,
            groupId: resolved.GroupId ?? externalId,
            group: resolved.Group,
            ct: ct
        );

        StampOwner(manifest);

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
        ValidateTrain(trainType, inputType);

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
            group: resolved.Group,
            ct: ct
        );

        StampOwner(manifest);

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
        ValidateTrain(trainType, inputType);

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
                var itemOptions = resolved.ManifestOptions.Copy();
                configureEach?.Invoke(source, itemOptions);

                var manifest = await context.UpsertManifestAsync(
                    trainType,
                    externalId,
                    input,
                    schedule,
                    itemOptions,
                    groupId: effectiveGroupId,
                    group: resolved.Group,
                    ct: ct
                );

                StampOwner(manifest);
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
                await PruneSafeAsync(
                    resolved.PrunePrefix,
                    resolved.BatchName is null ? null : effectiveGroupId,
                    keepIds,
                    ct
                );
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
        ValidateTrain(trainType, inputType);

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

                var itemOptions = resolved.ManifestOptions.Copy();
                configureEach?.Invoke(source, itemOptions);

                var manifest = await context.UpsertDependentManifestAsync(
                    trainType,
                    externalId,
                    input,
                    parentManifest.Id,
                    itemOptions,
                    groupId: effectiveGroupId,
                    group: resolved.Group,
                    ct: ct
                );

                StampOwner(manifest);
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
                await PruneSafeAsync(
                    resolved.PrunePrefix,
                    resolved.BatchName is null ? null : effectiveGroupId,
                    keepIds,
                    ct
                );
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
    public Task<DeadLetterOperationResult> RequeueDeadLetterAsync(
        long deadLetterId,
        CancellationToken ct = default
    ) => RequeueDeadLetterAsync(deadLetterId, askAfresh: false, ct);

    /// <inheritdoc />
    public async Task<DeadLetterOperationResult> RequeueDeadLetterAsync(
        long deadLetterId,
        bool askAfresh,
        CancellationToken ct = default
    )
    {
        await using var context = CreateContext();

        if (NoDispatcherRefusal(context) is { } noDispatcher)
            return new DeadLetterOperationResult(false, null, noDispatcher);

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

        var replayDecisionsOf = askAfresh
            ? null
            : await RetryReplay.SourceForRetryAsync(deadLetter.Manifest!, ct);
        var entry = CreateWorkQueueFromDeadLetter(deadLetter, replayDecisionsOf);
        context.WorkQueues.Add(entry);

        deadLetter.Requeue($"Re-queued (WorkQueue {entry.Id})");

        try
        {
            // A replay of the same run queued since the lookup: the entry asks afresh instead.
            await RetryReplayLinks.SaveAskingAfreshOnConflictAsync(context, [entry], logger, ct);
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
        if (NoteRefusal(note) is { } refusal)
            return new DeadLetterOperationResult(false, null, refusal);

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
    public Task<BatchDeadLetterResult> RequeueDeadLettersAsync(
        long[] deadLetterIds,
        CancellationToken ct = default
    ) => RequeueDeadLettersAsync(deadLetterIds, askAfresh: false, ct);

    /// <inheritdoc />
    public async Task<BatchDeadLetterResult> RequeueDeadLettersAsync(
        long[] deadLetterIds,
        bool askAfresh,
        CancellationToken ct = default
    )
    {
        if (OperationsService.BatchRefusal(deadLetterIds) is { } refused)
            return new BatchDeadLetterResult(0, refused);
        if (await NoDispatcherRefusalAsync() is { } noDispatcher)
            return new BatchDeadLetterResult(0, noDispatcher);

        var counts = await RequeueDeadLetterBatch(
            context =>
                context.DeadLetters.Where(d =>
                    deadLetterIds.Contains(d.Id)
                    && d.Status == DeadLetterStatus.AwaitingIntervention
                ),
            askAfresh,
            ct
        );

        return counts.ToResult();
    }

    /// <inheritdoc />
    public async Task<BatchDeadLetterResult> AcknowledgeDeadLettersAsync(
        long[] deadLetterIds,
        string note,
        CancellationToken ct = default
    )
    {
        if (OperationsService.BatchRefusal(deadLetterIds) is { } refused)
            return new BatchDeadLetterResult(0, refused);
        if (NoteRefusal(note) is { } refusal)
            return new BatchDeadLetterResult(0, refusal);

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
    public Task<BatchDeadLetterResult> RequeueAllDeadLettersAsync(CancellationToken ct = default) =>
        RequeueAllDeadLettersAsync(askAfresh: false, ct);

    /// <inheritdoc />
    public Task<BatchDeadLetterResult> RequeueAllDeadLettersAsync(
        bool askAfresh,
        CancellationToken ct = default
    ) => RequeueAllDeadLettersAsync(askAfresh, progress: null, ct);

    /// <inheritdoc />
    public async Task<BatchDeadLetterResult> RequeueAllDeadLettersAsync(
        bool askAfresh,
        IProgress<int>? progress,
        CancellationToken ct = default
    )
    {
        // A page at a time, by manifest rather than by dead letter, so every dead letter for one
        // manifest is in the same page and still folds into one entry. The cursor moves past a
        // page's manifests whether or not they were requeued, so one already queued is skipped
        // once rather than read again on every page.
        if (await NoDispatcherRefusalAsync() is { } noDispatcher)
            return new BatchDeadLetterResult(0, noDispatcher);

        var total = RequeueCounts.None;
        var after = long.MinValue;

        while (true)
        {
            List<long> manifestIds;
            await using (var context = CreateContext())
                manifestIds = await context
                    .DeadLetters.Where(d =>
                        d.Status == DeadLetterStatus.AwaitingIntervention && d.ManifestId > after
                    )
                    .Select(d => d.ManifestId)
                    .Distinct()
                    .OrderBy(id => id)
                    .Take(RequeueAllPageSize)
                    .ToListAsync(ct);

            if (manifestIds.Count == 0)
                break;

            total += await RequeueDeadLetterBatch(
                context =>
                    context.DeadLetters.Where(d =>
                        manifestIds.Contains(d.ManifestId)
                        && d.Status == DeadLetterStatus.AwaitingIntervention
                    ),
                askAfresh,
                ct
            );
            progress?.Report(total.Resolved);

            after = manifestIds[^1];
        }

        return total.ToResult();
    }

    /// <summary>
    /// How many manifests <see cref="RequeueAllDeadLettersAsync(bool, IProgress{int}, CancellationToken)"/> requeues per page. Test seam;
    /// one page is one batch, so it matches the operations surface's batch limit.
    /// </summary>
    internal int RequeueAllPageSize { get; set; } = OperationsService.MaxBatchSize;

    /// <summary>
    /// How many dead letters <see cref="AcknowledgeAllDeadLettersAsync"/> acknowledges per
    /// statement on a relational store. Test seam. Measured at 3.9 to 9.5 s per 500,000 rows, so a
    /// page stays far inside a 30 s command timeout.
    /// </summary>
    internal int AcknowledgeAllPageSize { get; set; } = 50_000;

    /// <summary>What a dead-letter requeue did, summed across the pages of a requeue-all.</summary>
    private readonly record struct RequeueCounts(int Resolved, int Entries, int Folded, int Skipped)
    {
        public static RequeueCounts None => default;

        public static RequeueCounts operator +(RequeueCounts a, RequeueCounts b) =>
            new(
                a.Resolved + b.Resolved,
                a.Entries + b.Entries,
                a.Folded + b.Folded,
                a.Skipped + b.Skipped
            );

        public BatchDeadLetterResult ToResult()
        {
            var message = $"{Resolved} dead letter(s) requeued";
            if (Folded > 0)
                message +=
                    $"; {Folded} folded into another dead letter's entry for the same manifest";
            if (Skipped > 0)
                message += $"; {Skipped} skipped because their manifest already has a queued entry";

            return new BatchDeadLetterResult(Resolved, message + ".");
        }
    }

    /// <inheritdoc />
    public async Task<BatchDeadLetterResult> AcknowledgeAllDeadLettersAsync(
        string note,
        CancellationToken ct = default
    )
    {
        if (NoteRefusal(note) is { } refusal)
            return new BatchDeadLetterResult(0, refusal);

        var acknowledged = 0;
        await using (var context = CreateContext())
        {
            if (!context.SupportsSetUpdates())
                acknowledged = await AcknowledgeAwaitingAsync(
                    context,
                    context.DeadLetters.Where(d =>
                        d.Status == DeadLetterStatus.AwaitingIntervention
                    ),
                    note,
                    ct
                );
            else
            {
                // A page of ids at a time, each its own statement, so no single update over a
                // large backlog outlasts the command timeout. The cursor is the page's last id,
                // read first, so each page is a range the primary key serves.
                var after = long.MinValue;
                while (true)
                {
                    var awaiting = context.DeadLetters.Where(d =>
                        d.Status == DeadLetterStatus.AwaitingIntervention && d.Id > after
                    );
                    var last = await awaiting
                        .OrderBy(d => d.Id)
                        .Skip(AcknowledgeAllPageSize - 1)
                        .Select(d => (long?)d.Id)
                        .FirstOrDefaultAsync(ct);

                    var page = last is { } upTo ? awaiting.Where(d => d.Id <= upTo) : awaiting;
                    acknowledged += await AcknowledgeAwaitingAsync(context, page, note, ct);

                    if (last is not { } next)
                        break;
                    after = next;
                }
            }
        }

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

        if (context.SupportsSetUpdates())
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
    /// one of them is resolved with a note naming the entry. The counts returned include both.
    /// </summary>
    /// <remarks>
    /// The "already queued?" check and the insert are separate statements, so a concurrent
    /// requeue or the ManifestManager can queue one of the manifests in between, and the insert
    /// then fails on the index. That is caught provider-neutrally: when the save fails and one of
    /// the manifests this attempt meant to queue now has a queued entry, the whole attempt is
    /// rolled back and rerun from a fresh read, which skips that manifest and no longer sees dead
    /// letters another requeue resolved. Any other failure is rethrown.
    /// </remarks>
    private async Task<RequeueCounts> RequeueDeadLetterBatch(
        Func<IDataContext, IQueryable<Effect.Models.DeadLetter.DeadLetter>> select,
        bool askAfresh,
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
                return await RequeueDeadLetterBatchOnce(
                    context,
                    deadLetters,
                    attempt == 1,
                    askAfresh,
                    ct
                );
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

    private async Task<RequeueCounts> RequeueDeadLetterBatchOnce(
        IDataContext context,
        List<Effect.Models.DeadLetter.DeadLetter> deadLetters,
        bool firstAttempt,
        bool askAfresh,
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
        var groups = deadLetters
            .Where(d => !alreadyQueued.Contains(d.ManifestId))
            .GroupBy(d => d.ManifestId)
            .Select(g => g.OrderByDescending(d => d.Id).ToList())
            .ToList();

        // A dead-letter requeue retries the manifest's failed run, so it replays that run's
        // decisions as the ManifestManager's retry does, under the same checks, looked up for the
        // whole page at once (docs/adr/0017). An operator who asks afresh gets no link.
        var replaySources = askAfresh
            ? new Dictionary<long, long>()
            : await RetryReplay.SourcesForRetriesAsync(
                groups.Select(g => g[0].Manifest!).ToList(),
                ct
            );

        var batches = groups
            .Select(members =>
                (
                    Entry: CreateWorkQueueFromDeadLetter(
                        members[0],
                        replaySources.TryGetValue(members[0].ManifestId, out var source)
                            ? source
                            : null
                    ),
                    Members: members
                )
            )
            .ToList();

        if (firstAttempt && BeforeRequeueInsert is { } hook)
            await hook(ct);

        foreach (var (entry, members) in batches)
        {
            context.WorkQueues.Add(entry);
            foreach (var dl in members)
                dl.Requeue("Re-queued (batch)");
        }

        // A replay of the same run queued since the lookup: that entry asks afresh instead.
        await RetryReplayLinks.SaveAskingAfreshOnConflictAsync(
            context,
            batches.Select(b => b.Entry).ToList(),
            logger,
            ct
        );

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

        return new RequeueCounts(resolved, batches.Count, folded, skipped);
    }

    private static WorkQueue CreateWorkQueueFromDeadLetter(
        Effect.Models.DeadLetter.DeadLetter deadLetter,
        long? replayDecisionsOf
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
                // An operator asked for this run by name, so it runs while the manifest is disabled.
                ExplicitTrigger = true,
                ReplayDecisionsOf = replayDecisionsOf,
            }
        );
    }

    /// <summary>
    /// Chooses the run a dead-letter requeue replays (docs/adr/0017). Built here rather than
    /// injected, so the public constructors stay as they are.
    /// </summary>
    private RetryDecisionReplay RetryReplay =>
        _retryReplay ??= new RetryDecisionReplay(dataContextFactory, logger);

    private RetryDecisionReplay? _retryReplay;

    // ── Private helpers ──────────────────────────────────────────────────

    /// <summary>
    /// <see cref="OperationsService.NoDispatcherMessage"/> when a dead letter requeued here would
    /// never run, or <c>null</c>: on a host that <c>AddScheduler()</c> built without a database
    /// provider, whose store is in this process's memory and which registers no job dispatcher
    /// (scheduler ADR 0019). A scheduler built by hand, or resolved on a host without
    /// <c>AddScheduler()</c>, does not know whether a database provider is registered, and is not
    /// refused.
    /// </summary>
    private string? NoDispatcherRefusal(IDataContext context) =>
        NothingDispatches && context is DbContext db && !db.Database.IsRelational()
            ? OperationsService.NoDispatcherMessage
            : null;

    private bool NothingDispatches =>
        configuration is { IsSchedulerHost: true, HasDatabaseProvider: false };

    /// <inheritdoc cref="NoDispatcherRefusal(IDataContext)"/>
    private async Task<string?> NoDispatcherRefusalAsync()
    {
        if (!NothingDispatches)
            return null;
        await using var context = CreateContext();
        return NoDispatcherRefusal(context);
    }

    private IDataContext CreateContext() =>
        dataContextFactory.Create() as IDataContext
        ?? throw new InvalidOperationException("Failed to create data context");

    private ResolvedOptions ResolveOptions(Action<ScheduleOptions>? options)
    {
        var opts = new ScheduleOptions();
        options?.Invoke(opts);

        var manifestOptions = opts.ToManifestOptions();

        // The scheduler-wide defaults, for a manifest whose options state neither. Resolved here,
        // before a batch copies the options per item, so configureEach reads the resolved value.
        manifestOptions._defaultMaxRetries = configuration?.DefaultMaxRetries;
        manifestOptions.MisfirePolicy ??= configuration?.DefaultMisfirePolicy;

        // A group of the manifest's own (no group name) or a named batch's own group has no other
        // members to disagree with, so the manifest's stated priority is the group's too.
        var ownsGroup =
            opts._groupId is null
            || (opts._batchName is not null && opts._groupId == opts._batchName);
        var group = opts._groupOptions;

        return new ResolvedOptions(
            ManifestOptions: manifestOptions,
            GroupId: opts._groupId,
            Group: new ManifestGroupSeed(
                Priority: group?._priority ?? (ownsGroup ? opts._priority : null),
                MaxActiveJobsStated: group?._maxActiveJobsStated ?? false,
                MaxActiveJobs: group?._maxActiveJobs,
                IsEnabled: group?._isEnabled,
                PriorityIfNew: manifestOptions.Priority
            ),
            PrunePrefix: opts._prunePrefix,
            BatchName: opts._batchName
        );
    }

    private static async Task<Manifest> GetManifestByExternalIdAsync(
        IDataContext context,
        string externalId,
        CancellationToken ct
    ) =>
        await context.Manifests.FirstOrDefaultAsync(m => m.ExternalId == externalId, ct)
        ?? throw new InvalidOperationException($"No manifest found with ExternalId '{externalId}'");

    private async Task PruneSafeAsync(
        string prunePrefix,
        string? batchGroup,
        System.Collections.Generic.HashSet<string> keepExternalIds,
        CancellationToken ct
    )
    {
        try
        {
            await using var pruneContext = CreateContext();
            await PruneStaleManifestsAsync(
                pruneContext,
                prunePrefix,
                batchGroup,
                keepExternalIds,
                ct
            );
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

    /// <summary>
    /// Deletes the manifests whose external ID starts with <paramref name="prunePrefix"/> and that
    /// this batch no longer declares. A named batch passes its group in
    /// <paramref name="batchGroup"/> and prunes only manifests of that group, so a batch named
    /// <c>sync</c> never prunes the manifests of one named <c>sync-users</c>.
    /// </summary>
    private async Task PruneStaleManifestsAsync(
        IDataContext context,
        string prunePrefix,
        string? batchGroup,
        System.Collections.Generic.HashSet<string> keepExternalIds,
        CancellationToken ct
    )
    {
        // Server compute: load prefixed manifest IDs, filter stale ones in C#.
        // Avoids a NOT IN(...) clause with many string parameters that can timeout
        // on low-resource Postgres instances during query planning.
        var prefixed = context.Manifests.Where(m => m.ExternalId.StartsWith(prunePrefix));
        if (batchGroup is not null)
            prefixed = prefixed.Where(m => m.ManifestGroup.Name == batchGroup);

        var prefixedManifests = await prefixed
            .Select(m => new { m.Id, m.ExternalId })
            .ToListAsync(ct);

        var staleManifestIds = prefixedManifests
            .Where(m => !keepExternalIds.Contains(m.ExternalId))
            .Select(m => m.Id)
            .ToList();

        if (staleManifestIds.Count == 0)
            return;

        var (pruned, kept) = await ManifestPruner.PruneAsync(context, staleManifestIds, logger, ct);

        logger.LogInformation(
            "Pruned {Count} stale manifests with prefix '{Prefix}' ({Kept} kept until their runs finish)",
            pruned,
            prunePrefix,
            kept
        );
    }

    private record ResolvedOptions(
        ManifestOptions ManifestOptions,
        string? GroupId,
        ManifestGroupSeed Group,
        string? PrunePrefix,
        string? BatchName
    );
}
