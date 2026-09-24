using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Trax.Core.Extensions;
using Trax.Effect.Configuration.TraxEffectConfiguration;
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
using Trax.Effect.Utils;
using Trax.Mediator.Configuration;
using Trax.Mediator.Exceptions;
using Trax.Mediator.Services.ConcurrencyLimiter;
using Trax.Mediator.Services.RunExecutor;
using Trax.Mediator.Services.TrainAuthorization;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrustedExecution;

namespace Trax.Mediator.Services.TrainExecution;

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
    /// Per-train-type cache of the concrete train's overridden <c>OnQueue</c> method, or null when
    /// the train does not override it. Trains that do not override it (the common case) skip
    /// resolution entirely, so the enqueue path stays as light as it was before the hook existed.
    /// The reflection runs once per type.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, MethodInfo?> OnQueueOverrideCache = new();

    /// <summary>
    /// Per-train-type cache of the concrete train's <c>DeferQueuePromotion</c> property. Only read
    /// for trains that actually override <c>OnQueue</c> — deferring promotion without a hook would
    /// stage an entry with nothing to wait for.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, PropertyInfo?> DeferPromotionCache = new();

    /// <summary>
    /// Per-train-type cache of the concrete train's overridden <c>QueueSubjectKey</c> method, or
    /// null when the train does not override it. Trains that do not override it are never resolved
    /// for it, so the common enqueue path is unchanged.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, MethodInfo?> SubjectKeyOverrideCache = new();

    public async Task<QueueTrainResult> QueueAsync(
        string trainName,
        string? inputJson,
        int priority = 0,
        DateTime? scheduledAt = null,
        CancellationToken ct = default
    )
    {
        var registration = FindTrain(trainName);
        await AuthorizeAsync(registration, ct);

        registration.ServiceType.FullName.AssertLoaded();

        // A run needs an input instance, and the runner refuses an entry that has none, so
        // storing null only deferred the failure to dispatch, where nobody who could fix it would
        // see it. No input is read as an empty object instead, which is refused here when the
        // input type needs values (see DeserializeInput).
        var missing = string.IsNullOrWhiteSpace(inputJson);
        var json = missing ? EmptyInput : inputJson!;

        EnforceInputSizeCap(json, registration);
        var input = DeserializeInput(json, registration, missing);

        var serializedInput = JsonSerializer.Serialize(
            input,
            registration.InputType,
            TraxJsonSerializationOptions.ManifestProperties
        );

        var deferPromotion = ResolveDeferPromotion(registration);

        var entry = WorkQueue.Create(
            new CreateWorkQueue
            {
                TrainName = registration.ServiceType.FullName,
                Input = serializedInput,
                InputTypeName = registration.InputType.FullName,
                Priority = priority,
                ScheduledAt = ToUtc(scheduledAt),
                DeferPromotion = deferPromotion,
            }
        );

        entry.SubjectKey = ResolveSubjectKey(registration, input, entry.ExternalId);

        if (deferPromotion)
            return await QueueWithDeferredPromotionAsync(registration, input, entry, ct);

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

                using (enqueueContext.Enter(dataContext))
                    await InvokeQueueHookAsync(registration, input, entry.ExternalId, ct);
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

    public async Task<RunTrainResult> RunAsync(
        string trainName,
        string inputJson,
        CancellationToken ct = default
    )
    {
        var registration = FindTrain(trainName);
        await AuthorizeAsync(registration, ct);

        // Read the same way QueueAsync reads it, so the two methods agree on a missing input.
        var missing = string.IsNullOrWhiteSpace(inputJson);
        var json = missing ? EmptyInput : inputJson!;

        EnforceInputSizeCap(json, registration);
        var input = DeserializeInput(json, registration, missing);

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
        object input,
        string externalId
    )
    {
        var subjectKey = SubjectKeyOverrideCache.GetOrAdd(
            registration.ImplementationType,
            static type =>
            {
                var method = type.GetMethod(
                    "QueueSubjectKey",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    [typeof(Metadata)]
                );

                var declaringType = method?.DeclaringType;
                if (declaringType is { IsGenericType: true })
                    declaringType = declaringType.GetGenericTypeDefinition();

                // Only a concrete override counts — the base returns null for every train.
                return declaringType != typeof(ServiceTrain<,>) ? method : null;
            }
        );

        if (subjectKey is null)
            return null;

        var train = serviceProvider.GetRequiredService(registration.ServiceType);

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
            key = (string?)subjectKey.Invoke(train, [keyMetadata]);
        }
        catch (TargetInvocationException ex)
        {
            // Unwrap so the caller sees the train's real exception, not the reflection wrapper.
            ExceptionDispatchInfo.Throw(ex.InnerException ?? ex);
            throw;
        }

        if (key is null)
            return null;

        // Empty is refused rather than treated as a subject: every train returning it would be
        // serialized against every other, and it is almost always an unset identity.
        if (key.Length == 0)
            throw new InvalidOperationException(
                $"{registration.ServiceTypeName}.QueueSubjectKey returned an empty key. Return "
                    + "null when the entry should not be serialized."
            );

        // The key is indexed. One too long for the index inserts fine while queued and then
        // fails the claim on every cycle, so it is refused here, where the caller sees it.
        if (key.Length > MaxSubjectKeyLength)
            throw new InvalidOperationException(
                $"{registration.ServiceTypeName}.QueueSubjectKey returned a key of {key.Length} "
                    + $"characters; the limit is {MaxSubjectKeyLength}. Use a record identity, or "
                    + "a hash of a longer one."
            );

        return key;
    }

    /// <summary>
    /// The longest subject key an enqueue accepts. Well inside the Postgres btree entry limit
    /// even when every character takes three bytes, the most a UTF-16 unit encodes to.
    /// </summary>
    private const int MaxSubjectKeyLength = WorkQueue.MaxSubjectKeyLength;

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
            await InvokeQueueHookAsync(registration, input, entry.ExternalId, ct);
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
    private bool ResolveDeferPromotion(TrainRegistration registration)
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

        var train = serviceProvider.GetRequiredService(registration.ServiceType);

        try
        {
            return property.GetValue(train) is true;
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
    /// Invokes the train's <c>OnQueue</c> hook if the concrete train overrides it. Resolves the
    /// train through its service interface (so <c>[Inject]</c> properties and <c>CanonicalName</c>
    /// are populated exactly as a normal run would), then calls the hook with a non-persisted
    /// metadata carrying the input, canonical name, and the work queue entry's ExternalId.
    /// Exceptions are intentionally not caught: a failed <c>OnQueue</c> aborts the enqueue.
    /// </summary>
    /// <remarks>
    /// The hook is invoked via reflection rather than a marker interface on purpose: a non-generic
    /// interface on <c>ServiceTrain&lt;,&gt;</c> would collide with the canonical-interface
    /// selection in train discovery (it picks the first non-generic interface), so every train
    /// could register under the marker instead of its own interface.
    /// </remarks>
    private async Task InvokeQueueHookAsync(
        TrainRegistration registration,
        object input,
        string externalId,
        CancellationToken ct
    )
    {
        var onQueue = ResolveOnQueueOverride(registration.ImplementationType);
        if (onQueue is null)
            return;

        var train = serviceProvider.GetRequiredService(registration.ServiceType);

        var hookMetadata = Metadata.Create(
            new CreateMetadata
            {
                Name = registration.ServiceType.FullName!,
                ExternalId = externalId,
                Input = input,
            }
        );

        try
        {
            await (Task)onQueue.Invoke(train, [hookMetadata, ct])!;
        }
        catch (TargetInvocationException ex)
        {
            // MethodInfo.Invoke wraps a synchronous throw from the hook. Unwrap so callers see
            // the hook's real exception (with its original stack trace), not the reflection wrapper.
            ExceptionDispatchInfo.Throw(ex.InnerException ?? ex);
        }
    }

    /// <summary>
    /// Returns the train's overridden <c>OnQueue</c> method, or null when the train does not
    /// override the no-op <c>ServiceTrain&lt;,&gt;.OnQueue</c>. Cached per type; the reflection
    /// runs once.
    /// </summary>
    private static MethodInfo? ResolveOnQueueOverride(Type implementationType) =>
        OnQueueOverrideCache.GetOrAdd(
            implementationType,
            static type =>
            {
                var method = type.GetMethod(
                    "OnQueue",
                    BindingFlags.Instance | BindingFlags.NonPublic,
                    [typeof(Metadata), typeof(CancellationToken)]
                );

                var declaringType = method?.DeclaringType;
                if (declaringType is { IsGenericType: true })
                    declaringType = declaringType.GetGenericTypeDefinition();

                return declaringType != typeof(ServiceTrain<,>) ? method : null;
            }
        );

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
            throw new InvalidOperationException(
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

    private void EnforceInputSizeCap(string inputJson, TrainRegistration registration)
    {
        // Enforced post-authorization so unauthenticated callers can't map the cap,
        // and pre-deserialization so oversized JSON never reaches the deserializer.
        // Byte length (UTF-8) is the bounded resource — char length would miscount
        // surrogate pairs and multi-byte sequences.
        var byteCount = System.Text.Encoding.UTF8.GetByteCount(inputJson);
        if (byteCount > mediatorConfiguration.MaxInputJsonBytes)
            throw new TrainInputValidationException(
                registration.ServiceTypeName,
                byteCount,
                mediatorConfiguration.MaxInputJsonBytes
            );
    }

    /// <summary>What a missing input is read as.</summary>
    private const string EmptyInput = "{}";

    private static object DeserializeInput(
        string inputJson,
        TrainRegistration registration,
        bool missing
    )
    {
        object? input;

        if (missing)
        {
            // A missing input stands in for an input with no values, which is only honest for a
            // type that needs none. System.Text.Json builds a positional record from {} with every
            // constructor parameter at its default, so without this a train taking
            // record RenamePlayer(string Id, string NewName) would be queued with a null Id.
            // Respecting required constructor parameters refuses exactly that, and leaves Unit,
            // an input with only settable properties, and parameters with defaults unaffected.
            try
            {
                input = JsonSerializer.Deserialize(
                    inputJson,
                    registration.InputType,
                    EmptyInputOptions()
                );
            }
            catch (JsonException refused)
            {
                throw new JsonException(
                    $"No input was given, and {registration.InputTypeName} cannot be built "
                        + $"without one: {refused.Message}",
                    refused
                );
            }
        }
        else
        {
            input = JsonSerializer.Deserialize(
                inputJson,
                registration.InputType,
                TraxEffectConfiguration.StaticSystemJsonSerializerOptions
            );
        }

        // A JSON null is well-formed but is not an input, so it is reported the way any other
        // input the train cannot use is: as a JSON problem the caller can fix.
        if (input is null)
            throw new JsonException(
                $"InputJson deserialized to null. Expected an instance of {registration.InputTypeName}."
            );

        return input;
    }

    private static (JsonSerializerOptions Source, JsonSerializerOptions Strict)? _emptyInputOptions;

    /// <summary>
    /// The system options with required constructor parameters respected, rebuilt only if the
    /// system options object itself is replaced.
    /// </summary>
    private static JsonSerializerOptions EmptyInputOptions()
    {
        var source = TraxEffectConfiguration.StaticSystemJsonSerializerOptions;
        var cached = _emptyInputOptions;

        if (cached is { } hit && ReferenceEquals(hit.Source, source))
            return hit.Strict;

        var strict = new JsonSerializerOptions(source)
        {
            RespectRequiredConstructorParameters = true,
        };
        _emptyInputOptions = (source, strict);
        return strict;
    }
}
