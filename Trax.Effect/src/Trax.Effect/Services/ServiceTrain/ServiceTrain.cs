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
using Trax.Effect.Services.LifecycleHookRunner;
using Trax.Effect.Services.StepEffectRunner;

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
    /// ParentId for the train, used to establish parent-child relationships between trains.
    /// </summary>
    internal long? ParentId { get; set; }

    /// <summary>
    /// The EffectRunner is responsible for managing all effect providers and persisting
    /// metadata to the underlying storage systems.
    /// </summary>
    [Inject]
    [JsonIgnore]
    public IEffectRunner? EffectRunner { get; set; }

    [Inject]
    [JsonIgnore]
    public IStepEffectRunner? StepEffectRunner { get; set; }

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
    /// Gets the canonical train name. Prefers the interface name set at registration time,
    /// falling back to the concrete type's FullName for trains resolved outside of DI.
    /// </summary>
    internal string TrainName =>
        CanonicalName
        ?? GetType().FullName
        ?? throw new TrainException($"Could not find FullName for ({GetType().Name})");

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
        StepEffectRunner.AssertLoaded();
        LifecycleHookRunner.AssertLoaded();
        ServiceProvider.AssertLoaded();

        if (Metadata == null)
            await this.InitializeServiceTrain();

        Metadata.AssertLoaded();
        await EffectRunner.SaveChanges(CancellationToken);

        await LifecycleHookRunner.OnStarted(Metadata, CancellationToken);

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
                    await LifecycleHookRunner.OnCancelled(Metadata, CancellationToken);
                else
                    await LifecycleHookRunner.OnFailed(Metadata, exception, CancellationToken);

                exception.Rethrow();
            }

            var output = result.Unwrap();
            Logger?.LogTrace("({TrainName}) completed successfully.", TrainName);
            Metadata.SetOutputObject(output);

            await EffectRunner.Update(Metadata);
            await this.FinishServiceTrain(result);
            await EffectRunner.SaveChanges(CancellationToken);

            await LifecycleHookRunner.OnCompleted(Metadata, CancellationToken);

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
                await LifecycleHookRunner.OnCancelled(Metadata, CancellationToken);
            else
                await LifecycleHookRunner.OnFailed(Metadata, e, CancellationToken);

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
    /// Creates a composable Monad helper with ServiceProvider for step DI.
    /// </summary>
    protected new Monad<TIn, TOut> Activate(TIn input, params object[] otherInputs) =>
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
        StepEffectRunner?.Dispose();
        LifecycleHookRunner?.Dispose();
        Metadata?.Dispose();

        Logger = null;
        ServiceProvider = null;
    }
}
