using System.Text.Json.Serialization;
using LanguageExt;
using LanguageExt.UnsafeValueAccess;
using Microsoft.Extensions.Logging;
using Trax.Core.Exceptions;
using Trax.Core.Extensions;
using Trax.Core.Monad;
using Trax.Core.Train;
using Trax.Effect.Attributes;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Models.Metadata.DTOs;
using Trax.Effect.Services.EffectRunner;
using Trax.Effect.Services.JunctionEffectRunner;
using Trax.Effect.Services.LifecycleHookRunner;

namespace Trax.Effect.Services.ServiceTrain;

/// <summary>
/// Extends the base Train class to add database tracking and logging capabilities.
/// This class automatically records execution details including inputs, outputs,
/// execution time, and error information to a persistent store.
/// </summary>
/// <typeparam name="TIn">The input type for the train</typeparam>
/// <typeparam name="TOut">The output type for the train</typeparam>
public abstract class ServiceTrain<TIn, TOut> : Train<TIn, TOut>, IServiceTrain<TIn, TOut>
{
    /// <summary>
    /// Database Metadata row associated with the train. Contains all tracking information
    /// about this execution including inputs, outputs, timing, and error details.
    /// </summary>
    [JsonIgnore]
    public Metadata? Metadata { get; internal set; }

    /// <summary>
    /// The parent metadata ID for this train, used to establish parent-child relationships
    /// between trains. Set automatically when a train is scheduled as a dependent of another train.
    /// </summary>
    public long? ParentId { get; internal set; }

    /// <summary>
    /// The EffectRunner is responsible for managing all effect providers and persisting
    /// metadata to the underlying storage systems.
    /// </summary>
    [Inject]
    [JsonIgnore]
    public IEffectRunner? EffectRunner { get; set; }

    [Inject]
    [JsonIgnore]
    public IJunctionEffectRunner? JunctionEffectRunner { get; set; }

    [Inject]
    [JsonIgnore]
    public ILifecycleHookRunner? LifecycleHookRunner { get; set; }

    /// <summary>
    /// Logger specific to this train type, used for recording diagnostic information.
    /// </summary>
    [Inject]
    [JsonIgnore]
    public ILogger<ServiceTrain<TIn, TOut>>? Logger { get; set; }

    /// <summary>
    /// The service provider used to resolve dependencies within the train.
    /// </summary>
    [Inject]
    [JsonIgnore]
    public IServiceProvider? ServiceProvider { get; set; }

    /// <summary>
    /// The canonical (interface) name for this train, set during DI registration.
    /// When null, falls back to the concrete type's FullName.
    /// </summary>
    [JsonIgnore]
    public string? CanonicalName { get; set; }

    /// <summary>
    /// Gets the typed input that was passed to this train. Set before the chain
    /// runs, so it is available in all lifecycle hooks: <see cref="OnStarted"/>,
    /// <see cref="OnCompleted"/>, <see cref="OnFailed"/>, and <see cref="OnCancelled"/>.
    /// </summary>
    /// <remarks>
    /// Throws <see cref="ChainDeclarationException"/> while the chain is being read, because
    /// <c>Junctions()</c> is then run to record its declaration and no input exists. Returns
    /// <c>default</c> whenever the train has no metadata carrying an input, which includes
    /// <see cref="QueueSubjectKey"/> and <see cref="OnQueue"/>: they are called on an instance
    /// that has not run, so read the input from their <c>metadata</c> argument instead.
    /// </remarks>
    protected TIn TrainInput =>
        IsDeclaringChain ? throw new ChainDeclarationException(TrainName, nameof(TrainInput))
        : Metadata is not null && Metadata.GetInputObject() is TIn typed ? typed
        : default!;

    /// <summary>
    /// Gets the typed output produced by this train. Set after a successful run, so it is
    /// only meaningful in <see cref="OnCompleted"/>. Returns <c>default</c> in
    /// <see cref="OnStarted"/>, <see cref="OnFailed"/>, and <see cref="OnCancelled"/>
    /// because the train either hasn't run yet, failed before producing output, or was cancelled.
    /// </summary>
    /// <remarks>
    /// Throws <see cref="ChainDeclarationException"/> while the chain is being read, because
    /// <c>Junctions()</c> is then run to record its declaration and no output exists. Returns
    /// <c>default</c> in <see cref="QueueSubjectKey"/> and <see cref="OnQueue"/>, which are
    /// called on an instance that has not run and has no metadata.
    /// </remarks>
    protected TOut TrainOutput =>
        IsDeclaringChain ? throw new ChainDeclarationException(TrainName, nameof(TrainOutput))
        : Metadata is not null && Metadata.GetOutputObject() is TOut typed ? typed
        : default!;

    /// <summary>
    /// Gets the canonical train name. Prefers the interface name set at registration time
    /// via <c>AddScopedTraxRoute</c>, falling back to the concrete type's FullName for
    /// trains resolved outside of DI.
    /// </summary>
    public string TrainName =>
        CanonicalName
        ?? GetType().FullName
        ?? throw new TrainException($"Could not find FullName for ({GetType().Name})");

    /// <summary>
    /// Called after the train's metadata is initialized and persisted, before the chain runs.
    /// Override to add per-train startup logic. Exceptions are caught and logged — they will not
    /// prevent the train from running.
    /// </summary>
    protected virtual Task OnStarted(Metadata metadata, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Called after a successful run, after output is persisted and global hooks have fired.
    /// Override to add per-train completion logic (e.g., notifications, cache invalidation).
    /// Exceptions are caught and logged — they will not cause the train to report failure.
    /// </summary>
    protected virtual Task OnCompleted(Metadata metadata, CancellationToken ct) =>
        Task.CompletedTask;

    /// <summary>
    /// Called after a failed run (non-cancellation exception), after failure state is persisted
    /// and global hooks have fired. Override to add per-train failure handling (e.g., alerting).
    /// Exceptions are caught and logged — they will not mask the original failure.
    /// </summary>
    protected virtual Task OnFailed(Metadata metadata, Exception exception, CancellationToken ct) =>
        Task.CompletedTask;

    /// <summary>
    /// Called after cancellation (OperationCanceledException), after cancellation state is persisted
    /// and global hooks have fired. Override to add per-train cancellation handling.
    /// Exceptions are caught and logged.
    /// </summary>
    protected virtual Task OnCancelled(Metadata metadata, CancellationToken ct) =>
        Task.CompletedTask;

    /// <summary>
    /// Identifies the thing this mutation touches, so two entries naming the same subject are not
    /// dispatched at the same time. Returns null by default, which means no serialization.
    /// </summary>
    /// <remarks>
    /// Called at enqueue time with a metadata carrying the input, so the key can vary per mutation
    /// rather than being fixed per train. Read it with <c>metadata.GetInput&lt;T&gt;()</c>.
    ///
    /// Throwing aborts the enqueue. That is deliberate: a key that cannot be computed must not
    /// silently become null, because that would drop the serialization guarantee at exactly the
    /// moment the caller was relying on it.
    ///
    /// The key is compared as an exact, case-sensitive string across every train, so two trains
    /// returning the same key serialize against each other. Prefix it with something the train
    /// owns when that is not what you want. Only entries created through the mediator's queue
    /// path carry a key; work queued from a manifest is not about a record and has no subject.
    /// </remarks>
    protected virtual string? QueueSubjectKey(Metadata metadata) => null;

    /// <summary>
    /// Whether this train's queue entry should be held unconfirmed until its <see cref="OnQueue"/>
    /// hook has returned. Defaults to false: the entry is dispatchable the moment it is written.
    /// </summary>
    /// <remarks>
    /// Override to true when the hook's side-effect lives outside Trax's own data context. A
    /// separate <c>DbContext</c> has its own connection and therefore its own transaction, so it
    /// cannot be rolled back with the entry. Deferring promotion does not make the two atomic, but
    /// it makes a failure between them findable: the entry is left unconfirmed instead of the
    /// side-effect being left with no entry, and the scheduler's stale-entry sweep resolves it.
    ///
    /// A deferring train's hook runs after its entry is committed, so there is no enqueue
    /// transaction to join and <c>IEnqueueContextAccessor.Current</c> is null inside it.
    ///
    /// Has no effect unless <see cref="OnQueue"/> is also overridden.
    /// </remarks>
    protected virtual bool DeferQueuePromotion => false;

    /// <summary>
    /// Called synchronously at ENQUEUE time, inside the mediator's queue path. Does NOT fire on
    /// the synchronous run path, and does NOT fire again when the background dispatcher later runs
    /// the train.
    /// </summary>
    /// <remarks>
    /// For most trains the hook runs before the work queue row is committed, and writes it tracks
    /// on <c>IEnqueueContextAccessor.Current</c> commit in the same transaction as the row. For a
    /// train with <see cref="DeferQueuePromotion"/> set, the row is committed first, unconfirmed,
    /// and confirmed once the hook returns.
    ///
    /// Unlike <see cref="OnStarted"/>/<see cref="OnCompleted"/>/<see cref="OnFailed"/>, an
    /// exception thrown here is NOT swallowed: it propagates out of the enqueue call and aborts
    /// the enqueue, leaving no dispatchable work queue row. Use this only for work that must
    /// succeed for the mutation to be accepted.
    ///
    /// Idempotency: the deferred background run re-executes the full <c>Junctions()</c> chain,
    /// so any effect you perform here will be performed again by the chain when the job runs.
    /// Make it idempotent.
    ///
    /// The train is not initialized at enqueue time, so <see cref="Metadata"/> and
    /// <c>TrainInput</c> are unavailable. Read everything from the passed <paramref name="metadata"/>:
    /// the input via <c>metadata.GetInput&lt;T&gt;()</c>, and <c>metadata.ExternalId</c> to
    /// correlate with the eventual run (the run executes under the same ExternalId). <c>Id</c>,
    /// <c>ManifestId</c>, and <c>ScheduledTime</c> are unset because no run exists yet.
    /// </remarks>
    protected virtual Task OnQueue(Metadata metadata, CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Overrides the base Train Run method to add database tracking and logging capabilities.
    /// </summary>
    /// <param name="input">The input data for the train</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests</param>
    /// <returns>The result of the train execution</returns>
    public override async Task<TOut> Run(TIn input, CancellationToken cancellationToken = default)
    {
        CancellationToken = cancellationToken;

        EffectRunner.AssertLoaded();
        JunctionEffectRunner.AssertLoaded();
        LifecycleHookRunner.AssertLoaded();
        ServiceProvider.AssertLoaded();

        if (Metadata == null)
            await this.InitializeServiceTrain();

        Metadata.AssertLoaded();
        await EffectRunner.SaveChanges(CancellationToken);

        // Make the typed input available to lifecycle hooks before any of them fire, so
        // OnStarted observes the same input as OnCompleted/OnFailed. This sets the in-memory
        // object only; the input column is persisted later, so the initial Pending row above
        // is unchanged.
        Metadata.SetInputObject(input);

        // Everything up to the result is captured rather than allowed to propagate, so a failure
        // takes exactly one path: the terminal write and the failure hooks run once. Rethrowing
        // from inside a try whose catch also finishes the train ran both of them twice.
        Either<Exception, TOut> result;

        try
        {
            await LifecycleHookRunner.OnStarted(Metadata, CancellationToken);

            try
            {
                await OnStarted(Metadata, CancellationToken);
            }
            catch (Exception hookEx)
            {
                Logger?.LogError(
                    hookEx,
                    "Train-level OnStarted hook threw for train ({TrainName}).",
                    TrainName
                );
            }

            Logger?.LogTrace("Running Train: ({TrainName})", TrainName);
            result = await RunEither(input);
        }
        catch (Exception e)
        {
            result = e;
        }

        if (result.IsLeft)
        {
            var exception = result.Swap().ValueUnsafe();
            Logger?.LogError(
                "Caught Exception ({Type}) with Message ({Message}).",
                exception.GetType(),
                exception.Message
            );

            // The train's own failure is what the caller needs. If recording it fails too, that
            // is logged and the failure still propagates, with its hooks; the stale-run reaper
            // fails a row left in progress.
            try
            {
                await this.FinishServiceTrain(result);
                await SaveOutcome();
            }
            catch (Exception recordEx)
            {
                Logger?.LogError(
                    recordEx,
                    "Could not record the failure of train ({TrainName}); the original failure still propagates.",
                    TrainName
                );
            }

            if (exception is OperationCanceledException)
            {
                await LifecycleHookRunner.OnCancelled(Metadata, CancellationToken);

                try
                {
                    await OnCancelled(Metadata, CancellationToken);
                }
                catch (Exception hookEx)
                {
                    Logger?.LogError(
                        hookEx,
                        "Train-level OnCancelled hook threw for train ({TrainName}).",
                        TrainName
                    );
                }
            }
            else
            {
                await LifecycleHookRunner.OnFailed(Metadata, exception, CancellationToken);

                try
                {
                    await OnFailed(Metadata, exception, CancellationToken);
                }
                catch (Exception hookEx)
                {
                    Logger?.LogError(
                        hookEx,
                        "Train-level OnFailed hook threw for train ({TrainName}).",
                        TrainName
                    );
                }
            }

            exception.Rethrow();
        }

        var output = result.Unwrap();
        Logger?.LogTrace("({TrainName}) completed successfully.", TrainName);
        Metadata.SetOutputObject(output);

        // A failure to record a completed run propagates as it is. It is not turned into a
        // Failed outcome: the work happened, and recording that it failed would be false.
        await EffectRunner.Update(Metadata);
        await this.FinishServiceTrain(result);
        await SaveOutcome();

        // Ensure output is available as serialized JSON for lifecycle hooks,
        // even when SaveTrainParameters() is not configured. Runs AFTER
        // SaveChanges() so it is NOT persisted to the database.
        if (Metadata.Output is null)
        {
            var outputObject = Metadata.GetOutputObject();
            if (outputObject is not null)
            {
                try
                {
                    Metadata.Output = System.Text.Json.JsonSerializer.Serialize(
                        (object)outputObject,
                        Configuration
                            .TraxEffectConfiguration
                            .TraxEffectConfiguration
                            .StaticSystemJsonSerializerOptions
                    );
                }
                catch (Exception ex)
                {
                    Logger?.LogDebug(
                        ex,
                        "Failed to serialize output for lifecycle hooks in train ({TrainName}).",
                        TrainName
                    );
                }
            }
        }

        await LifecycleHookRunner.OnCompleted(Metadata, CancellationToken);

        try
        {
            await OnCompleted(Metadata, CancellationToken);
        }
        catch (Exception hookEx)
        {
            Logger?.LogError(
                hookEx,
                "Train-level OnCompleted hook threw for train ({TrainName}).",
                TrainName
            );
        }

        return output;
    }

    /// <summary>
    /// Persists the train's terminal state.
    /// </summary>
    /// <remarks>
    /// Deliberately not given the caller's token. That token is cancelled in exactly the case
    /// this write exists to record, so handing it over would abandon the row at
    /// <c>InProgress</c> with no <c>EndTime</c> — an execution that finished but cannot say how,
    /// and one a scheduler's stale-in-progress reaper later rewrites to <c>Failed</c> whatever
    /// actually happened. The outcome is the audit record of the work, not part of the work the
    /// caller is entitled to cancel.
    /// </remarks>
    private Task SaveOutcome()
    {
        EffectRunner.AssertLoaded();

        return EffectRunner.SaveChanges(CancellationToken.None);
    }

    public virtual async Task<TOut> Run(TIn input, Metadata metadata)
    {
        await this.InitializeServiceTrain(metadata);
        return await Run(input, CancellationToken);
    }

    /// <summary>
    /// Executes the train with the given input, pre-created metadata, and cancellation support.
    /// </summary>
    public virtual async Task<TOut> Run(
        TIn input,
        Metadata metadata,
        CancellationToken cancellationToken
    )
    {
        CancellationToken = cancellationToken;
        await this.InitializeServiceTrain(metadata);
        return await Run(input, CancellationToken);
    }

    /// <summary>
    /// Supplies the container alongside the train, so junctions named by the chain resolve
    /// through dependency injection rather than needing a parameterless constructor.
    /// </summary>
    protected override Monad<TIn, TOut> NewMonad() =>
        new(this, ServiceProvider!, CancellationToken);

    public void Dispose()
    {
        if (Metadata != null)
        {
            Metadata.SetInputObject(null);
            Metadata.SetOutputObject(null);
        }

        EffectRunner?.Dispose();
        JunctionEffectRunner?.Dispose();
        LifecycleHookRunner?.Dispose();
        Metadata?.Dispose();

        Logger = null;
        ServiceProvider = null;
    }
}
