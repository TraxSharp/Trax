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
    /// Gets the canonical train name. Prefers the interface name set at registration time
    /// via <c>AddScopedTraxRoute</c>, falling back to the concrete type's FullName for
    /// trains resolved outside of DI.
    /// </summary>
    public string TrainName =>
        CanonicalName
        ?? GetType().FullName
        ?? throw new TrainException($"Could not find FullName for ({GetType().Name})");

    /// <summary>
    /// Called after the train's metadata is initialized and persisted, before RunInternal executes.
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

        try
        {
            Logger?.LogTrace("Running Train: ({TrainName})", TrainName);
            Metadata.SetInputObject(input);
            var result = await RunInternal(input);

            if (result.IsLeft)
            {
                var exception = result.Swap().ValueUnsafe();
                Logger?.LogError(
                    "Caught Exception ({Type}) with Message ({Message}).",
                    exception.GetType(),
                    exception.Message
                );
                await this.FinishServiceTrain(result);
                await EffectRunner.SaveChanges(CancellationToken);

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

            await EffectRunner.Update(Metadata);
            await this.FinishServiceTrain(result);
            await EffectRunner.SaveChanges(CancellationToken);

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
        catch (Exception e)
        {
            Logger?.LogError(
                "Caught Exception ({Type}) with Message ({Message}).",
                e.GetType(),
                e.Message
            );

            await this.FinishServiceTrain(e);
            await EffectRunner.SaveChanges(CancellationToken);

            if (e is OperationCanceledException)
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
                await LifecycleHookRunner.OnFailed(Metadata, e, CancellationToken);

                try
                {
                    await OnFailed(Metadata, e, CancellationToken);
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

            throw;
        }
    }

    public virtual async Task<TOut> Run(TIn input, Metadata metadata)
    {
        await this.InitializeServiceTrain(metadata);
        return await Run(input);
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
        return await Run(input);
    }

    /// <summary>
    /// Abstract method that must be implemented by concrete train classes.
    /// This method contains the core business logic.
    /// </summary>
    protected abstract override Task<Either<Exception, TOut>> RunInternal(TIn input);

    /// <summary>
    /// Creates a composable Monad helper with ServiceProvider for junction DI.
    /// Overrides the base Train.Activate to inject the ServiceProvider, enabling
    /// automatic dependency resolution for junctions via the Chain API.
    /// </summary>
    /// <param name="input">The primary input for the train</param>
    /// <param name="otherInputs">Additional objects to store in the Monad's Memory</param>
    /// <returns>A Monad instance for method chaining with DI support</returns>
    public new Monad<TIn, TOut> Activate(TIn input, params object[] otherInputs) =>
        new Monad<TIn, TOut>(this, ServiceProvider!, CancellationToken).Activate(
            input,
            otherInputs
        );

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
