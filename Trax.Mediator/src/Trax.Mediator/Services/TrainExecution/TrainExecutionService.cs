using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Extensions;
using Trax.Effect.Data.Services.DataContext;
using Trax.Effect.Data.Services.DataContextTransaction;
using Trax.Effect.Data.Services.EnqueueContext;
using Trax.Effect.Data.Services.IDataContextFactory;
using Trax.Effect.Data.Services.WorkQueuePromotion;
using Trax.Effect.Enums;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Models.WorkQueue;
using Trax.Effect.Models.WorkQueue.DTOs;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Configuration;
using Trax.Mediator.Exceptions;
using Trax.Mediator.Services.ConcurrencyLimiter;
using Trax.Mediator.Services.RunExecutor;
using Trax.Mediator.Services.TrainAuthorization;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrustedExecution;

namespace Trax.Mediator.Services.TrainExecution;

/// <summary>
/// Default <see cref="ITrainExecutionService"/>, registered as scoped by <c>AddMediator</c>. Resolve
/// <see cref="ITrainExecutionService"/> rather than constructing this; it is infrastructure not
/// intended to be constructed directly.
/// </summary>
/// <param name="discoveryService">Resolves train names to registrations.</param>
/// <param name="runExecutor">Executes <see cref="RunAsync"/>, locally or remotely.</param>
/// <param name="concurrencyLimiter">Gates <see cref="RunAsync"/>; queued work is not gated here.</param>
/// <param name="dataContextFactory">Opens the contexts the enqueue writes through.</param>
/// <param name="mediatorConfiguration">Supplies the input size cap, hook limit and authorization opt-out.</param>
/// <param name="serviceProvider">
/// The caller's scope. Resolves the train for its queue hooks (in a child scope), the optional
/// <see cref="TrainAuthorization.ITrainAuthorizationService"/> and
/// <see cref="TrustedExecution.ITrustedExecutionScope"/>, and the enqueue helpers.
/// </param>
public class TrainExecutionService(
    ITrainDiscoveryService discoveryService,
    IRunExecutor runExecutor,
    IConcurrencyLimiter concurrencyLimiter,
    IDataContextProviderFactory dataContextFactory,
    MediatorConfiguration mediatorConfiguration,
    IServiceProvider serviceProvider
) : ITrainExecutionService
{
    /// <summary>
    /// Per-train-type cache of the concrete train's <c>DeferQueuePromotion</c> property. Only read
    /// for trains that actually override <c>OnQueue</c> — deferring promotion without a hook would
    /// stage an entry with nothing to wait for.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> DeferPromotionCache = new();

    /// <summary>
    /// The enqueue whose <c>OnQueue</c> hook is running on this async flow, which an enqueue
    /// started from inside that hook joins. Static so every instance of this scoped service sees
    /// it, the same way <c>EnqueueContextAccessor</c> keeps its value; each enqueue only joins one
    /// that writes to the same database (see <see cref="FindEnqueueToJoin"/>).
    /// </summary>
    private static readonly AsyncLocal<JoinableEnqueue?> RunningEnqueue = new();

    /// <summary>
    /// Per-train-type cache of <c>ServiceTrain.EnterQueueHooks</c>, which the enqueue calls around
    /// both queue hooks. Only read for a train that overrides one of them.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, MethodInfo> EnterQueueHooksCache = new();

    /// <inheritdoc/>
    public Task<QueueTrainResult> QueueAsync(
        string trainName,
        string? inputJson,
        int priority = 0,
        DateTime? scheduledAt = null,
        CancellationToken ct = default
    ) =>
        QueueAsync(
            trainName,
            inputJson,
            new QueueTrainOptions { Priority = priority, ScheduledAt = scheduledAt },
            ct
        );

    /// <inheritdoc/>
    public async Task<QueueTrainResult> QueueAsync(
        string trainName,
        string? inputJson,
        QueueTrainOptions options,
        CancellationToken ct = default
    )
    {
        ArgumentNullException.ThrowIfNull(options);
        var priority = options.Priority;
        var scheduledAt = options.ScheduledAt;

        var (registration, input) = await PrepareCoreAsync(trainName, inputJson, ct);

        registration.ServiceType.FullName.AssertLoaded();

        // Measured before the entry exists: the stored form is written indented and with every
        // member present, so it is capped too, not only the caller's text.
        var serializedInput = TrainInputReader.WriteForStorage(
            input,
            registration,
            mediatorConfiguration.MaxInputJsonBytes
        );

        // The train is resolved at most once per enqueue, in a scope of its own that is disposed
        // when the enqueue returns. The caller's scope can live as long as a Blazor circuit, so
        // resolving from it kept every train and its scoped dependencies alive for the life of
        // the tab, and a scoped DbContext a hook left a failed write in failed every later
        // enqueue of the train from that tab.
        await using var train = new EnqueueTrain(serviceProvider, registration);

        // An enqueue started from inside another train's OnQueue hook joins that enqueue's
        // transaction, so it commits with the outer entry or not at all (mediator/0003).
        var outer = FindEnqueueToJoin();
        using var joined = outer?.TryJoin();
        if (joined is null)
            outer = null;

        // A deferring train that joins is not staged: nothing outside the outer transaction can
        // see its entry until that commits, so there is no window for staging to cover.
        var declaresDeferral = ResolveDeferPromotion(registration, train);
        var deferPromotion = declaresDeferral && outer is null;

        // The key is resolved before the entry exists and handed to WorkQueue.Create, so Create's
        // own checks see it. The train computes it from the entry's ExternalId, which Create
        // would otherwise generate, so the enqueue chooses it first (in Create's format) and
        // stamps it on the entry: CreateWorkQueue has no ExternalId to pass it through.
        var externalId = Guid.NewGuid().ToString("N");
        var subjectKey = ResolveSubjectKey(registration, train, input, externalId);

        var entry = CreateEntry(
            registration,
            new CreateWorkQueue
            {
                TrainName = registration.ServiceType.FullName,
                Input = serializedInput,
                InputTypeName = registration.InputType.FullName,
                Priority = priority,
                ScheduledAt = ToUtc(scheduledAt),
                DeferPromotion = deferPromotion,
                SubjectKey = subjectKey,
                ReplayDecisionsOf = options.ReplayDecisionsOf,
            }
        );
        entry.ExternalId = externalId;

        if (outer is not null)
            return await QueueIntoOuterAsync(
                outer,
                registration,
                train,
                input,
                entry,
                declaresDeferral,
                ct
            );

        if (deferPromotion)
            return await QueueWithDeferredPromotionAsync(registration, train, input, entry, ct);

        using var dataContext = await dataContextFactory.CreateDbContextAsync(ct);

        // Queue-time hook: fire OnQueue before the work queue row is inserted, so a consumer
        // can perform a side-effect (e.g. an optimistic shadow write) the moment the mutation
        // is accepted. The entry's ExternalId is the correlation key; the eventual run executes
        // under the same ExternalId. Exceptions propagate and abort the enqueue.
        //
        // Track() is change-tracking only, so the row still has not been INSERTed when the hook
        // runs. Tracking it first lets the hook's own writes join the same SaveChanges, and the
        // transaction below makes the pair atomic: a hook that throws after writing leaves
        // nothing behind, which is the crash window this path used to carry. A hook that writes
        // through its OWN DbContext is not covered: a separately-pooled context has its own
        // connection and therefore its own transaction.
        // Only a train with a hook needs the transaction: without one there is a single write,
        // and an explicit BEGIN/COMMIT would add two round trips to every enqueue for nothing.
        var hasHook = ResolveOnQueueOverride(registration.ImplementationType) is not null;
        using var transaction = hasHook ? await TryBeginTransactionAsync(dataContext, ct) : null;
        try
        {
            await dataContext.Track(entry);

            if (hasHook)
            {
                // Resolved rather than injected so the public constructor signature is
                // unchanged: adding a parameter to a public service type is a breaking API
                // change for anyone constructing it directly.
                var enqueueContext = serviceProvider.GetRequiredService<IEnqueueContextAccessor>();

                // Only a real transaction can take a nested enqueue with it, so a provider that
                // has none leaves nested enqueues to commit on their own, as they always did.
                var joinable = transaction is null
                    ? null
                    : new JoinableEnqueue(dataContext, dataContextFactory);

                using (enqueueContext.Enter(dataContext))
                using (joinable?.Enter())
                {
                    try
                    {
                        await InvokeQueueHookWithinLimitAsync(
                            registration,
                            train,
                            input,
                            entry.ExternalId,
                            joinable,
                            ct
                        );
                    }
                    finally
                    {
                        joinable?.Close();
                    }
                }

                joinable?.ThrowIfNestedIncomplete(registration);
            }

            await dataContext.SaveChanges(ct);

            if (transaction is not null)
                await transaction.Commit();
        }
        catch
        {
            if (transaction is not null)
                await RollbackQuietlyAsync(transaction);

            throw;
        }

        return new QueueTrainResult(entry.Id, entry.ExternalId);
    }

    /// <summary>
    /// The enqueue this one should join: the one whose hook is running on this async flow, when
    /// it writes to the same database through the same factory. Another host's enqueue, reached
    /// through a hook that calls into a second container, is not joined.
    /// </summary>
    private JoinableEnqueue? FindEnqueueToJoin() =>
        RunningEnqueue.Value is { } running && ReferenceEquals(running.Factory, dataContextFactory)
            ? running
            : null;

    /// <summary>
    /// Writes an enqueue started from inside another train's <c>OnQueue</c> hook into the outer
    /// enqueue's context and transaction. The entry is flushed so the caller gets its id, but it
    /// commits or rolls back with the outer entry. Its own hook runs as it would at the top level,
    /// and any enqueue that hook starts joins the same outer enqueue.
    /// </summary>
    /// <remarks>
    /// A failure is recorded on the outer enqueue before it propagates, because the hook that
    /// called this may catch it and carry on: the failed attempt can have left tracked or flushed
    /// writes on the shared context, and committing them would be a partial nested enqueue.
    /// </remarks>
    private async Task<QueueTrainResult> QueueIntoOuterAsync(
        JoinableEnqueue outer,
        TrainRegistration registration,
        EnqueueTrain train,
        object input,
        WorkQueue entry,
        bool declaresDeferral,
        CancellationToken ct
    )
    {
        try
        {
            await outer.WithContextAsync(context => context.Track(entry));

            if (ResolveOnQueueOverride(registration.ImplementationType) is not null)
            {
                var enqueueContext = serviceProvider.GetRequiredService<IEnqueueContextAccessor>();

                // A deferring train's hook is documented to see no enqueue context, whoever
                // enqueued it, so the one the outer hook entered is cleared for it. Its writes go
                // through a context of its own, which is what deferral exists for.
                using (
                    declaresDeferral
                        ? enqueueContext.Suppress()
                        : enqueueContext.Enter(outer.Context)
                )
                    await InvokeQueueHookAsync(registration, train, input, entry.ExternalId, ct);
            }

            await outer.WithContextAsync(context => context.SaveChanges(ct));
        }
        catch (Exception failure)
        {
            outer.RecordFailure(failure);
            throw;
        }

        return new QueueTrainResult(entry.Id, entry.ExternalId);
    }

    /// <inheritdoc/>
    public async Task<RunTrainResult> RunAsync(
        string trainName,
        string inputJson,
        CancellationToken ct = default
    )
    {
        var (registration, input) = await PrepareCoreAsync(trainName, inputJson, ct);

        registration.ServiceType.FullName.AssertLoaded();

        using var permit = await concurrencyLimiter.AcquireAsync(
            registration.ServiceType.FullName,
            ct
        );

        return await runExecutor.ExecuteAsync(
            registration.ServiceType.FullName,
            input,
            registration.OutputType,
            ct
        );
    }

    /// <inheritdoc/>
    public async Task<PreparedTrain> PrepareAsync(
        string trainName,
        string? inputJson,
        CancellationToken ct = default
    )
    {
        var (registration, input) = await PrepareCoreAsync(trainName, inputJson, ct);
        return new PreparedTrain(registration, input);
    }

    /// <summary>
    /// The lookup, authorization and input reading every entry point shares, so a queue, a run and
    /// a surface that submits work itself agree on the same name and JSON.
    /// </summary>
    private async Task<(TrainRegistration Registration, object Input)> PrepareCoreAsync(
        string trainName,
        string? inputJson,
        CancellationToken ct
    )
    {
        var registration = FindTrain(trainName);

        // Before the input is read, so a caller who may not use the train learns nothing about
        // its input from a parse error.
        await AuthorizeAsync(registration, ct);

        // A run needs an input instance, and the runner refuses an entry that has none, so
        // storing null only deferred the failure to dispatch, where nobody who could fix it would
        // see it. No input is read as an empty object instead, which is refused when the input
        // type needs values (see TrainInputReader.Read).
        var input = TrainInputReader.Read(
            inputJson,
            registration,
            mediatorConfiguration.MaxInputJsonBytes
        );

        return (registration, input);
    }

    /// <summary>
    /// Asks the train what this mutation touches, so dispatch can keep two entries naming the same
    /// subject from running at once. Null for every train that does not override
    /// <c>QueueSubjectKey</c>, which is the default.
    /// </summary>
    /// <remarks>
    /// A throw propagates and aborts the enqueue rather than degrading to null: a key that cannot
    /// be computed means the caller's serialization guarantee cannot be honoured, and failing
    /// loudly is better than quietly running the work unserialized.
    /// </remarks>
    private string? ResolveSubjectKey(
        TrainRegistration registration,
        EnqueueTrain train,
        object input,
        string externalId
    )
    {
        // Trains that do not override it are never resolved for it, so the common enqueue path
        // is unchanged. Discovery reports the same answer as TrainRegistration.HasQueueSubjectKey.
        var subjectKey = QueueMemberOverrides.QueueSubjectKey(registration.ImplementationType);

        if (subjectKey is null)
            return null;

        var keyMetadata = Metadata.Create(
            new CreateMetadata
            {
                Name = registration.ServiceType.FullName!,
                ExternalId = externalId,
                Input = input,
            }
        );

        string? key;

        try
        {
            using (train.EnterQueueHooks(keyMetadata))
                key = (string?)subjectKey.Invoke(train.Instance, [keyMetadata]);
        }
        catch (TargetInvocationException ex)
        {
            // Unwrap so the caller sees the train's real exception, not the reflection wrapper.
            ExceptionDispatchInfo.Throw(ex.InnerException ?? ex);
            throw;
        }

        return key;
    }

    /// <summary>
    /// Builds the entry. <c>WorkQueue.Create</c> owns the subject-key rules (not empty or
    /// whitespace, no unpaired surrogate, at most <c>WorkQueue.MaxSubjectKeyLength</c> Unicode
    /// characters), so the mediator keeps no copy of them: it names the train in Create's refusal
    /// and throws it as the <see cref="InvalidOperationException"/> an enqueue refusal has always
    /// been.
    /// </summary>
    private static WorkQueue CreateEntry(TrainRegistration registration, CreateWorkQueue dto)
    {
        try
        {
            return WorkQueue.Create(dto);
        }
        catch (ArgumentException refused) when (dto.SubjectKey is not null)
        {
            var reason = refused.ParamName is { } param
                ? refused.Message.Replace($" (Parameter '{param}')", "")
                : refused.Message;

            throw new InvalidOperationException(
                $"{registration.ServiceTypeName}.QueueSubjectKey returned a key the work queue "
                    + $"cannot use. {reason}",
                refused
            );
        }
    }

    /// <summary>
    /// A scheduled time stored as UTC whatever it arrived as. Local times are converted; an
    /// unspecified kind is taken to already be UTC, which is how a timestamp without an offset
    /// arrives from JSON. Npgsql refuses a non-UTC value for a timestamptz column, while the
    /// other providers store it as given, so without this the providers disagree.
    /// </summary>
    private static DateTime? ToUtc(DateTime? value) =>
        value?.Kind switch
        {
            DateTimeKind.Local => value.Value.ToUniversalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value.Value, DateTimeKind.Utc),
            _ => value,
        };

    /// <summary>
    /// The two-phase enqueue, used when a train defers promotion: commit the entry unconfirmed,
    /// run the hook, then promote in a second commit.
    /// </summary>
    /// <remarks>
    /// The phases are deliberately separate commits. A hook whose side-effect lives in another
    /// database cannot join Trax's transaction, so the pair cannot be made atomic — but staging the
    /// entry first means a crash between the two leaves an unconfirmed entry, which the
    /// scheduler's stale-entry sweep finds and resolves (see docs/0018), instead of a side-effect
    /// that nothing will ever consume.
    ///
    /// A hook that <em>throws</em> still aborts the enqueue outright: the staged entry is removed,
    /// so the observable contract is unchanged.
    /// </remarks>
    private async Task<QueueTrainResult> QueueWithDeferredPromotionAsync(
        TrainRegistration registration,
        EnqueueTrain train,
        object input,
        WorkQueue entry,
        CancellationToken ct
    )
    {
        using (var stagingContext = await dataContextFactory.CreateDbContextAsync(ct))
        {
            await stagingContext.Track(entry);
            await stagingContext.SaveChanges(ct);
        }

        try
        {
            // No enqueue context: the entry is already committed, so there is no transaction for
            // the hook to join. Cleared rather than left alone because an enqueue started from
            // inside another train's hook would otherwise see that train's context.
            var enqueueContext = serviceProvider.GetRequiredService<IEnqueueContextAccessor>();

            using (enqueueContext.Suppress())
                await InvokeQueueHookAsync(registration, train, input, entry.ExternalId, ct);
        }
        catch (Exception hookFailure)
        {
            // Not the caller's token: a caller that gave up is the likeliest reason the hook
            // threw, and a staged entry left behind would be a rejected mutation still waiting.
            // A failure to remove it must not hide why the enqueue failed; the stale-entry sweep
            // resolves an entry left behind.
            try
            {
                await RemoveStagedEntryAsync(entry.Id);
            }
            catch
            {
                // The hook's exception is the one the caller needs.
            }

            ExceptionDispatchInfo.Throw(hookFailure);
            throw;
        }

        // Once the hook has returned, the mutation is accepted and its side-effect may have
        // landed. Confirming the entry is the other half of that, so it does not take the
        // caller's token either; see effect/0005 for the same reasoning on a run's outcome.
        var promotion = serviceProvider.GetRequiredService<IWorkQueuePromotion>();

        // False means the entry stopped being a staged, queued entry while the hook ran: an
        // operator cancelled it, or the stale-entry sweep resolved it because the hook outlived
        // StaleStagedEntryTimeout. The hook's side-effect may have landed but the work will not
        // run, and reporting success would say otherwise.
        if (
            !await promotion.PromoteAsync(entry.Id, CancellationToken.None)
            && !await WasConfirmedElsewhereAsync(entry.Id)
        )
            throw new QueuedWorkCancelledException(
                entry.Id,
                registration.ServiceType.FullName ?? registration.ServiceTypeName
            );

        return new QueueTrainResult(entry.Id, entry.ExternalId);
    }

    /// <summary>
    /// Whether an entry this enqueue could not promote was confirmed by something else, which
    /// a host that opted into promoting stale entries does when a hook outlives the timeout. Such
    /// an entry will run, so the enqueue succeeded.
    /// </summary>
    private async Task<bool> WasConfirmedElsewhereAsync(long workQueueId)
    {
        using var context = await dataContextFactory.CreateDbContextAsync(CancellationToken.None);

        return await context
            .WorkQueues.AsNoTracking()
            .AnyAsync(
                w =>
                    w.Id == workQueueId
                    && w.ConfirmedAt != null
                    && w.Status != WorkQueueStatus.Cancelled,
                CancellationToken.None
            );
    }

    /// <summary>
    /// Removes an entry staged for a hook that then threw, so a rejected mutation leaves no trace.
    /// </summary>
    private async Task RemoveStagedEntryAsync(long workQueueId)
    {
        using var context = await dataContextFactory.CreateDbContextAsync(CancellationToken.None);

        // Only an entry that is still staged. One the sweep promoted may already be dispatched
        // and running, and deleting it would orphan the run and free its subject.
        if (context is DbContext db && !db.Database.IsRelational())
        {
            // The in-memory provider does not translate ExecuteDelete.
            var staged = await db.Set<WorkQueue>()
                .FirstOrDefaultAsync(
                    w =>
                        w.Id == workQueueId
                        && w.ConfirmedAt == null
                        && w.Status == WorkQueueStatus.Queued,
                    CancellationToken.None
                );

            if (staged is not null)
            {
                db.Remove(staged);
                await db.SaveChangesAsync(CancellationToken.None);
            }

            return;
        }

        await context
            .WorkQueues.Where(w =>
                w.Id == workQueueId && w.ConfirmedAt == null && w.Status == WorkQueueStatus.Queued
            )
            .ExecuteDeleteAsync(CancellationToken.None);
    }

    /// <summary>
    /// Whether this train holds its queue entry unconfirmed until <c>OnQueue</c> has committed.
    /// False for every train that does not override the hook, so the common path is untouched.
    /// </summary>
    private static bool ResolveDeferPromotion(TrainRegistration registration, EnqueueTrain train)
    {
        if (ResolveOnQueueOverride(registration.ImplementationType) is null)
            return false;

        var property = DeferPromotionCache.GetOrAdd(
            registration.ImplementationType,
            static type =>
                type.GetProperty(
                    "DeferQueuePromotion",
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public
                )
        );

        if (property is null)
            return false;

        try
        {
            return property.GetValue(train.Instance) is true;
        }
        catch (TargetInvocationException ex)
        {
            // Unwrap so the caller sees the train's real exception, not the reflection wrapper.
            // OperationsService reports this message to GraphQL clients verbatim.
            ExceptionDispatchInfo.Throw(ex.InnerException ?? ex);
            throw;
        }
    }

    /// <summary>
    /// Begins a transaction for the enqueue, or returns null when beginning one throws
    /// <see cref="InvalidOperationException"/> or <see cref="NotSupportedException"/>, as a
    /// provider without transactions does unless told otherwise. Trax's in-memory provider is not
    /// such a case: <c>InMemoryContextProviderFactory</c> ignores EF's
    /// <c>TransactionIgnoredWarning</c>, so the call succeeds and returns a transaction whose
    /// commit and rollback do nothing. Either way the queue row and any ambient-context write still
    /// share one <c>SaveChanges</c>; they are simply not wrapped in a real transaction.
    /// </summary>
    private static async Task<IDataContextTransaction?> TryBeginTransactionAsync(
        IDataContext dataContext,
        CancellationToken ct
    )
    {
        try
        {
            return await dataContext.BeginTransaction(ct);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Rolls back an enqueue that failed, without letting the rollback's own failure replace the
    /// reason the enqueue failed.
    /// </summary>
    /// <remarks>
    /// The two arrive together: a connection that drops mid-write fails the <c>SaveChanges</c> and
    /// then the rollback, and the caller needs the first. The deferred path already guards its
    /// cleanup for the same reason ("A failure to remove it must not hide why the enqueue
    /// failed"), so both paths now answer the same way. The rollback's failure is logged rather
    /// than dropped: it usually means the transaction is gone with its connection, which an
    /// operator reading a failed enqueue wants to know.
    /// </remarks>
    private async Task RollbackQuietlyAsync(IDataContextTransaction transaction)
    {
        try
        {
            await transaction.Rollback();
        }
        catch (Exception rollbackFailure)
        {
            // Resolved rather than injected: adding a constructor parameter to a public service
            // type is a breaking change for anyone constructing it directly.
            serviceProvider
                .GetService<ILogger<TrainExecutionService>>()
                ?.LogWarning(
                    rollbackFailure,
                    "Rolling back a failed enqueue threw. The enqueue's own failure is the one "
                        + "reported to the caller."
                );
        }
    }

    /// <summary>
    /// Invokes the train's <c>OnQueue</c> hook if the concrete train overrides it. Uses the
    /// enqueue's train, resolved through its service interface (so <c>[Inject]</c> properties and <c>CanonicalName</c>
    /// are populated exactly as a normal run would), then calls the hook with a non-persisted
    /// metadata carrying the input, canonical name, and the work queue entry's ExternalId.
    /// Exceptions are intentionally not caught: a failed <c>OnQueue</c> aborts the enqueue.
    /// </summary>
    /// <remarks>
    /// The hook is invoked via reflection, not through a marker interface. Registration would not
    /// mistake a marker for the train's service type, since it selects the interface that derives
    /// from <c>IServiceTrain&lt;,&gt;</c>, but reflection finds only trains that override the
    /// hook, so a train without one is never resolved for it.
    /// </remarks>
    private static async Task InvokeQueueHookAsync(
        TrainRegistration registration,
        EnqueueTrain train,
        object input,
        string externalId,
        CancellationToken ct
    )
    {
        var onQueue = ResolveOnQueueOverride(registration.ImplementationType);
        if (onQueue is null)
            return;

        var hookMetadata = Metadata.Create(
            new CreateMetadata
            {
                Name = registration.ServiceType.FullName!,
                ExternalId = externalId,
                Input = input,
            }
        );

        // Entered here, in the method that awaits the hook, so the input flows with the hook's
        // own continuations and is gone once it returns (central ADR 0021).
        using var hookInput = train.EnterQueueHooks(hookMetadata);

        try
        {
            await (Task)onQueue.Invoke(train.Instance, [hookMetadata, ct])!;
        }
        catch (TargetInvocationException ex)
        {
            // MethodInfo.Invoke wraps a synchronous throw from the hook. Unwrap so callers see
            // the hook's real exception (with its original stack trace), not the reflection wrapper.
            ExceptionDispatchInfo.Throw(ex.InnerException ?? ex);
        }
    }

    /// <summary>
    /// Runs the hook of an enqueue that holds a connection and an open transaction, for at most
    /// <see cref="MediatorConfiguration.MaxQueueHookDuration"/>. Past it the hook's token is
    /// cancelled and the enqueue stops waiting, whether or not the hook stops, and throws
    /// <see cref="QueueHookTimeoutException"/>, so the caller rolls back and releases the
    /// connection (mediator/0004).
    /// </summary>
    /// <remarks>
    /// The enqueue also stops waiting when the caller cancels, and the hook may still be running
    /// then too. Either way, a hook still running after its enqueue stopped waiting has lost that
    /// enqueue: an enqueue it starts is refused rather than committing on its own, because the
    /// caller was told this one failed. Whatever it later throws is logged, since nobody is left
    /// to observe it.
    /// </remarks>
    private async Task InvokeQueueHookWithinLimitAsync(
        TrainRegistration registration,
        EnqueueTrain train,
        object input,
        string externalId,
        JoinableEnqueue? joinable,
        CancellationToken ct
    )
    {
        var limit = mediatorConfiguration.MaxQueueHookDuration;

        if (limit == Timeout.InfiniteTimeSpan)
        {
            await InvokeQueueHookAsync(registration, train, input, externalId, ct);
            return;
        }

        using var limited = CancellationTokenSource.CreateLinkedTokenSource(ct);
        limited.CancelAfter(limit);

        var hook = InvokeQueueHookAsync(registration, train, input, externalId, limited.Token);

        try
        {
            await hook.WaitAsync(limited.Token);
        }
        catch (OperationCanceledException)
            when (limited.IsCancellationRequested && !ct.IsCancellationRequested)
        {
            joinable?.Abandon();

            var trainName = registration.ServiceType.FullName ?? registration.ServiceTypeName;
            var logger = serviceProvider.GetService<ILogger<TrainExecutionService>>();

            logger?.LogWarning(
                "{Train}.OnQueue ran past MaxQueueHookDuration ({Limit}); its enqueue was "
                    + "rolled back.",
                trainName,
                limit
            );

            LogWhenAbandonedHookFaults(hook, trainName, logger, "failed on MaxQueueHookDuration");

            throw new QueueHookTimeoutException(trainName, limit);
        }
        catch (OperationCanceledException) when (!hook.IsCompleted)
        {
            // The caller cancelled, and the hook has not stopped. The caller is told the enqueue
            // failed and the transaction rolls back, so the hook has nothing left to join: an
            // enqueue it starts from here on is refused, the same as after the limit.
            joinable?.Abandon();

            var trainName = registration.ServiceType.FullName ?? registration.ServiceTypeName;
            var logger = serviceProvider.GetService<ILogger<TrainExecutionService>>();

            LogWhenAbandonedHookFaults(hook, trainName, logger, "been cancelled by its caller");

            throw;
        }
    }

    /// <summary>
    /// Logs what a hook throws after its enqueue stopped waiting for it, since nothing else
    /// observes that task any more.
    /// </summary>
    private static void LogWhenAbandonedHookFaults(
        Task hook,
        string trainName,
        ILogger? logger,
        string outcome
    )
    {
        if (hook.IsCompleted)
            return;

        _ = hook.ContinueWith(
            abandoned =>
                logger?.LogWarning(
                    abandoned.Exception,
                    "{Train}.OnQueue threw after its enqueue had already {Outcome}.",
                    trainName,
                    outcome
                ),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default
        );
    }

    /// <summary>
    /// Returns the train's overridden <c>OnQueue</c> method, or null when the train does not
    /// override the no-op <c>ServiceTrain&lt;,&gt;.OnQueue</c>. Trains that do not override it (the
    /// common case) skip resolution entirely, so the enqueue path stays as light as it was before
    /// the hook existed.
    /// </summary>
    private static MethodInfo? ResolveOnQueueOverride(Type implementationType) =>
        QueueMemberOverrides.OnQueue(implementationType);

    private async Task AuthorizeAsync(TrainRegistration registration, CancellationToken ct)
    {
        var authService = serviceProvider.GetService<ITrainAuthorizationService>();

        if (authService is not null)
        {
            await authService.AuthorizeAsync(registration, ct);
            return;
        }

        // Trusted infrastructure (a scheduler pipeline, a remote job runner, the dashboard's
        // admin surface) was authorized at its own gate, and an enforcer would skip it too.
        if (serviceProvider.GetService<ITrustedExecutionScope>() is { IsTrusted: true })
            return;

        // Fail closed: if the train carries auth requirements but no enforcer is
        // registered, refuse to execute. Hosts that genuinely run no API submissions
        // (e.g. scheduler-only processes) can opt out via
        // TraxMediatorBuilder.AllowMissingAuthorizationService().
        if (
            registration.HasAuthorizeAttribute
            && !mediatorConfiguration.AllowMissingAuthorizationService
        )
        {
            throw new TrainAuthorizationNotConfiguredException(
                registration.ServiceType.FullName ?? registration.ServiceTypeName,
                $"Train '{registration.ServiceTypeName}' declares [TraxAuthorize] but no "
                    + "ITrainAuthorizationService is registered. Call AddTraxApi() (or register "
                    + "a custom ITrainAuthorizationService) before building the host. If this "
                    + "process intentionally runs no authorized submissions, opt out with "
                    + "AddMediator(m => m.AllowMissingAuthorizationService())."
            );
        }
    }

    private TrainRegistration FindTrain(string trainName)
    {
        var trains = discoveryService.DiscoverTrains();

        var byFullName = trains.FirstOrDefault(t => t.ServiceType.FullName == trainName);
        if (byFullName is not null)
            return byFullName;

        var byFriendlyName = trains.Where(t => t.ServiceTypeName == trainName).ToList();
        if (byFriendlyName.Count == 1)
            return byFriendlyName[0];
        if (byFriendlyName.Count > 1)
            throw new AmbiguousTrainNameException(
                trainName,
                byFriendlyName.Select(t => t.ServiceType.FullName ?? t.ServiceTypeName).ToList()
            );

        throw new TrainNotFoundException(trainName);
    }

    /// <summary>
    /// The train one enqueue reads its subject key, its deferral flag and its hook from: resolved
    /// through its service interface on first use, in a scope created for this enqueue, and
    /// disposed with that scope when the enqueue returns. A train that overrides neither member
    /// is never resolved and no scope is created.
    /// </summary>
    /// <remarks>
    /// Resolving from the caller's scope instead, as this once did, parked a transient train graph
    /// (its <c>EffectRunner</c> and data context included) in that scope three times per enqueue,
    /// and let the scoped services a hook takes carry state from one enqueue into the next.
    /// See mediator/0002.
    /// </remarks>
    private sealed class EnqueueTrain(
        IServiceProvider callerServices,
        TrainRegistration registration
    ) : IAsyncDisposable
    {
        private AsyncServiceScope? _scope;
        private object? _instance;

        public object Instance
        {
            get
            {
                if (_instance is not null)
                    return _instance;

                _scope = callerServices.CreateAsyncScope();
                _instance = _scope.Value.ServiceProvider.GetRequiredService(
                    registration.ServiceType
                );
                return _instance;
            }
        }

        /// <summary>
        /// Hands the train the input <paramref name="metadata"/> carries, so <c>TrainInput</c>
        /// reads it inside <c>QueueSubjectKey</c> and <c>OnQueue</c> until the returned scope is
        /// disposed. Never assigns the train's <c>Metadata</c>: a later run on the same instance
        /// initializes its own (central ADR 0021).
        /// </summary>
        public IDisposable EnterQueueHooks(Metadata metadata)
        {
            var enter = EnterQueueHooksCache.GetOrAdd(
                registration.ImplementationType,
                static type =>
                    type.GetMethod(
                        nameof(ServiceTrain<object, object>.EnterQueueHooks),
                        BindingFlags.Instance | BindingFlags.Public,
                        [typeof(Metadata)]
                    )
                    ?? throw new InvalidOperationException(
                        $"{type.FullName} has no EnterQueueHooks(Metadata); every ServiceTrain "
                            + "does, so this train does not derive from one."
                    )
            );

            try
            {
                return (IDisposable)enter.Invoke(Instance, [metadata])!;
            }
            catch (TargetInvocationException ex)
            {
                ExceptionDispatchInfo.Throw(ex.InnerException ?? ex);
                throw;
            }
        }

        public ValueTask DisposeAsync() => _scope?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    /// <summary>
    /// An enqueue whose <c>OnQueue</c> hook is running inside a transaction, which enqueues
    /// started from that hook join (mediator/0003).
    /// </summary>
    /// <remarks>
    /// Nested enqueues a hook runs concurrently share one <see cref="IDataContext"/>, which is not
    /// safe for concurrent use, so the writes this service makes on it are serialized. Hooks
    /// themselves are not: the lock is never held while one runs, so a nested hook that enqueues
    /// again cannot deadlock on it.
    /// </remarks>
    private sealed class JoinableEnqueue(IDataContext context, IDataContextProviderFactory factory)
    {
        private readonly SemaphoreSlim _writes = new(1, 1);
        private readonly object _state = new();
        private int _inFlight;
        private bool _closed;
        private bool _abandoned;
        private Exception? _failure;

        public IDataContext Context { get; } = context;

        public IDataContextProviderFactory Factory { get; } = factory;

        /// <summary>Makes this the enqueue that hooks on this async flow join.</summary>
        public IDisposable Enter()
        {
            var previous = RunningEnqueue.Value;
            RunningEnqueue.Value = this;
            return new Restore(previous);
        }

        /// <summary>
        /// Counts a nested enqueue in until the returned handle is disposed, or returns null when
        /// the outer hook has already returned. An enqueue a hook started without awaiting, which
        /// only begins after that, has nothing left to join and commits on its own.
        /// </summary>
        public IDisposable? TryJoin()
        {
            lock (_state)
            {
                if (_abandoned)
                    throw new InvalidOperationException(
                        "This enqueue was started from an OnQueue hook whose own enqueue already "
                            + "failed while the hook was still running (it ran past "
                            + "MaxQueueHookDuration, or its caller cancelled) and was rolled back, "
                            + "so there is nothing for it to join, and committing on its own would "
                            + "queue work for a mutation the caller was told failed."
                    );

                if (_closed)
                    return null;

                _inFlight++;
            }

            return new Leave(this);
        }

        public async Task WithContextAsync(Func<IDataContext, Task> write)
        {
            await _writes.WaitAsync();
            try
            {
                await write(Context);
            }
            finally
            {
                _writes.Release();
            }
        }

        public void RecordFailure(Exception failure)
        {
            lock (_state)
                _failure ??= failure;
        }

        /// <summary>Ends joining: the outer hook has returned or thrown.</summary>
        public void Close()
        {
            lock (_state)
                _closed = true;
        }

        /// <summary>
        /// Ends joining for good: the enqueue stopped waiting for the outer hook while it was
        /// still running (the hook ran past its limit, or the caller cancelled), so anything it
        /// enqueues from now on is refused.
        /// </summary>
        public void Abandon()
        {
            lock (_state)
            {
                _closed = true;
                _abandoned = true;
            }
        }

        /// <summary>
        /// Refuses the outer enqueue when a nested one failed or is still running, since either
        /// leaves the shared context holding writes nobody finished.
        /// </summary>
        public void ThrowIfNestedIncomplete(TrainRegistration outer)
        {
            Exception? failure;
            int inFlight;

            lock (_state)
            {
                failure = _failure;
                inFlight = _inFlight;
            }

            if (failure is not null)
                throw new InvalidOperationException(
                    $"A train enqueued inside {outer.ServiceTypeName}.OnQueue failed, so the "
                        + "enqueue it joined is rolled back with it. See the inner exception.",
                    failure
                );

            if (inFlight > 0)
                throw new InvalidOperationException(
                    $"{outer.ServiceTypeName}.OnQueue returned while an enqueue it started was "
                        + "still running. A hook must await the enqueues it starts, so they can "
                        + "commit or roll back with the enqueue that ran it."
                );
        }

        private sealed class Leave(JoinableEnqueue enqueue) : IDisposable
        {
            private int _left;

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _left, 1) == 1)
                    return;

                lock (enqueue._state)
                    enqueue._inFlight--;
            }
        }

        private sealed class Restore(JoinableEnqueue? previous) : IDisposable
        {
            public void Dispose() => RunningEnqueue.Value = previous;
        }
    }
}
