using System.Collections.Concurrent;
using System.Reflection;
using LanguageExt;
using LanguageExt.ClassInstances;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Exceptions;
using Trax.Core.Route;
using Trax.Effect.Enums;
using Trax.Effect.Extensions;
using Trax.Effect.Models.Metadata;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainRegistry;

namespace Trax.Mediator.Services.TrainBus;

/// <summary>
/// Implements the train bus that dynamically executes trains based on their input type.
/// </summary>
/// <remarks>
/// The TrainBus class provides the core implementation of the mediator pattern for trains.
/// It uses reflection and dependency injection to dynamically discover, instantiate, and execute
/// the appropriate train for a given input type.
///
/// Each <c>RunAsync</c> call creates a child DI scope, resolves the train from that scope,
/// executes it, and disposes the scope when done. This ensures each train execution is fully
/// isolated — scoped services like <c>DbContext</c> are not shared across train executions.
/// This is especially important in Blazor Server where the circuit-level scope persists for
/// the entire connection.
///
/// The train bus relies on the train registry to map input types to train types,
/// and uses the service provider to resolve and instantiate the train instances.
///
/// This implementation supports:
/// - Dynamic train discovery based on input type
/// - Automatic dependency injection for train instances
/// - Property injection for trains
/// - Metadata association for tracking and logging
/// - Type-safe execution with generic output types
/// - Per-execution scope isolation
///
/// The train bus is registered as a scoped service in the dependency injection
/// container, allowing it to be injected into controllers, services, or other components
/// that need to execute trains.
/// </remarks>
/// <param name="serviceProvider">The service provider used to resolve train instances for <see cref="InitializeTrain"/></param>
/// <param name="scopeFactory">Factory for creating child scopes per <c>RunAsync</c> call</param>
/// <param name="registryService">The registry service that maps input types to train types</param>
public class TrainBus(
    IServiceProvider serviceProvider,
    IServiceScopeFactory scopeFactory,
    ITrainRegistry registryService
) : ITrainBus
{
    /// <summary>
    /// Thread-safe cache for storing reflection method lookups to improve performance.
    /// </summary>
    /// <remarks>
    /// This cache stores MethodInfo objects keyed by train type to avoid repeated reflection operations.
    /// Using ConcurrentDictionary ensures thread-safety for scoped service usage.
    /// </remarks>
    private static readonly ConcurrentDictionary<Type, MethodInfo> RunMethodCache = new();

    /// <summary>
    /// Thread-safe cache for storing the 2-parameter Run(TIn, Metadata) method lookups.
    /// </summary>
    /// <remarks>
    /// This cache is used when executing trains with pre-created metadata (e.g., from the scheduler).
    /// </remarks>
    private static readonly ConcurrentDictionary<Type, MethodInfo> RunWithMetadataMethodCache =
        new();

    /// <summary>
    /// Thread-safe cache for storing the 2-parameter Run(TIn, CancellationToken) method lookups.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, MethodInfo> RunWithCtMethodCache = new();

    /// <summary>
    /// Thread-safe cache for storing the 3-parameter Run(TIn, Metadata, CancellationToken) method lookups.
    /// </summary>
    private static readonly ConcurrentDictionary<Type, MethodInfo> RunWithMetadataCtMethodCache =
        new();

    /// <summary>
    /// Clears the static reflection method cache. This should be called during testing
    /// or when memory cleanup is needed to prevent memory leaks from cached MethodInfo objects.
    /// </summary>
    /// <remarks>
    /// This method is primarily intended for testing scenarios where multiple train types
    /// are created and discarded, potentially causing memory leaks through the static cache.
    /// In production, the cache should generally be left intact for performance benefits.
    /// </remarks>
    public static void ClearMethodCache()
    {
        RunMethodCache.Clear();
        RunWithMetadataMethodCache.Clear();
        RunWithCtMethodCache.Clear();
        RunWithMetadataCtMethodCache.Clear();
    }

    /// <summary>
    /// Gets the current size of the method cache for monitoring purposes.
    /// </summary>
    /// <returns>The number of cached method entries</returns>
    public static int GetMethodCacheSize()
    {
        return RunMethodCache.Count
            + RunWithMetadataMethodCache.Count
            + RunWithCtMethodCache.Count
            + RunWithMetadataCtMethodCache.Count;
    }

    public object InitializeTrain(object trainInput)
    {
        return InitializeTrainFromProvider(serviceProvider, trainInput);
    }

    /// <summary>
    /// Resolves and initializes a train from a specific service provider.
    /// Used by <c>RunAsync</c> to resolve trains from child scopes, and by
    /// <see cref="InitializeTrain"/> for backward-compatible resolution from the TrainBus's own scope.
    /// </summary>
    private static object InitializeTrainFromProvider(
        IServiceProvider provider,
        object trainInput,
        ITrainRegistry registry
    )
    {
        if (trainInput == null)
            throw new TrainException("trainInput is null as input to TrainBus.SendAsync(...)");

        // The full type of the input, rather than just the interface
        var inputType = trainInput.GetType();

        var foundTrain = registry.InputTypeToTrain.TryGetValue(inputType, out var correctTrain);

        if (foundTrain == false || correctTrain == null)
            throw new TrainException($"Could not find train with input type ({inputType.Name})");

        var trainService = provider.GetRequiredService(correctTrain);
        provider.InjectProperties(trainService);

        return trainService;
    }

    /// <summary>
    /// Instance helper that delegates to the static method with this bus's registry.
    /// </summary>
    private object InitializeTrainFromProvider(IServiceProvider provider, object trainInput)
    {
        return InitializeTrainFromProvider(provider, trainInput, registryService);
    }

    /// <summary>
    /// Executes a train that accepts the specified input type and returns the specified output type.
    /// Creates a child DI scope for the duration of the train execution.
    /// </summary>
    /// <typeparam name="TOut">The expected output type of the train</typeparam>
    /// <param name="trainInput">The input object for the train</param>
    /// <param name="metadata">Optional metadata to associate with the train execution</param>
    /// <returns>A task that resolves to the train's output</returns>
    /// <exception cref="TrainException">
    /// Thrown when the input is null, no train is found for the input type,
    /// the Run method cannot be found on the train, or the Run method invocation fails.
    /// </exception>
    public async Task<TOut> RunAsync<TOut>(object trainInput, Metadata? metadata = null)
    {
        using var scope = scopeFactory.CreateScope();
        var trainService = InitializeTrainFromProvider(scope.ServiceProvider, trainInput);
        var trainType = trainService.GetType();

        // When metadata is pre-created and Pending (from the scheduler or ad-hoc dashboard execution),
        // use the 2-param Run(input, metadata) method so the train uses this
        // pre-created metadata instead of creating a new one.
        if (metadata != null)
        {
            if (metadata.TrainState != TrainState.Pending)
                throw new TrainException(
                    $"TrainBus will not run a passed Metadata with state ({metadata.TrainState}), Must be Pending"
                );

            var runWithMetadataMethod = RunWithMetadataMethodCache.GetOrAdd(
                trainType,
                type =>
                {
                    var method = type.GetMethods()
                        .Where(x => x.Name == "Run")
                        // Run(input, metadata) has 2 parameters
                        .Where(x => x.GetParameters().Length == 2)
                        .FirstOrDefault(x =>
                            x.GetParameters()[1].ParameterType == typeof(Metadata)
                        );

                    if (method == null)
                        throw new TrainException(
                            $"Failed to find Run(input, metadata) method for train type ({type.Name})"
                        );

                    return method;
                }
            );

            var taskWithMetadata = (Task<TOut>?)
                runWithMetadataMethod.Invoke(trainService, [trainInput, metadata]);

            if (taskWithMetadata is null)
                throw new TrainException(
                    $"Failed to invoke Run(input, metadata) method for train type ({trainType.Name})"
                );

            return await taskWithMetadata;
        }

        // Standard case: Get the 1-param Run method from cache
        var runMethod = RunMethodCache.GetOrAdd(
            trainType,
            type =>
            {
                var method = type.GetMethods()
                    // Run function on Train/ServiceTrain
                    .Where(x => x.Name == "Run")
                    // Run(input, cancellationToken = default) has 2 parameters (second is optional CancellationToken).
                    .Where(x =>
                    {
                        var parameters = x.GetParameters();
                        return parameters.Length == 2
                            && parameters[1].ParameterType == typeof(CancellationToken);
                    })
                    // Run has an implementation in Trax.Core and Trax.Effect (the latter with the "new" keyword).
                    // We want the one from Trax.Effect
                    .FirstOrDefault(x => x.Module.Name.Contains("Effect"));

                if (method == null)
                    throw new TrainException(
                        $"Failed to find Run method for train type ({type.Name})"
                    );

                return method;
            }
        );

        // And finally run the train, casting the return type to preserve type safety.
        var taskRunMethod = (Task<TOut>?)
            runMethod.Invoke(trainService, [trainInput, CancellationToken.None]);

        if (taskRunMethod is null)
            throw new TrainException(
                $"Failed to invoke Run method for train type ({trainService.GetType().Name})"
            );

        return await taskRunMethod;
    }

    /// <summary>
    /// Executes a train with cancellation support.
    /// Creates a child DI scope for the duration of the train execution.
    /// </summary>
    public async Task<TOut> RunAsync<TOut>(
        object trainInput,
        CancellationToken cancellationToken,
        Metadata? metadata = null
    )
    {
        using var scope = scopeFactory.CreateScope();
        var trainService = InitializeTrainFromProvider(scope.ServiceProvider, trainInput);
        var trainType = trainService.GetType();

        if (metadata != null)
        {
            if (metadata.TrainState != TrainState.Pending)
                throw new TrainException(
                    $"TrainBus will not run a passed Metadata with state ({metadata.TrainState}), Must be Pending"
                );

            // Find Run(input, metadata, ct) — 3 params
            var runWithMetadataCtMethod = RunWithMetadataCtMethodCache.GetOrAdd(
                trainType,
                type =>
                {
                    var method = type.GetMethods()
                        .Where(x => x.Name == "Run")
                        .Where(x => x.GetParameters().Length == 3)
                        .FirstOrDefault(x =>
                            x.GetParameters()[1].ParameterType == typeof(Metadata)
                            && x.GetParameters()[2].ParameterType == typeof(CancellationToken)
                        );

                    if (method == null)
                        throw new TrainException(
                            $"Failed to find Run(input, metadata, ct) method for train type ({type.Name})"
                        );

                    return method;
                }
            );

            var taskWithMetadata = (Task<TOut>?)
                runWithMetadataCtMethod.Invoke(
                    trainService,
                    [trainInput, metadata, cancellationToken]
                );

            if (taskWithMetadata is null)
                throw new TrainException(
                    $"Failed to invoke Run(input, metadata, ct) method for train type ({trainType.Name})"
                );

            return await taskWithMetadata;
        }

        // Find Run(input, ct) — 2 params where second is CancellationToken
        var runWithCtMethod = RunWithCtMethodCache.GetOrAdd(
            trainType,
            type =>
            {
                var method = type.GetMethods()
                    .Where(x => x.Name == "Run")
                    .Where(x => x.GetParameters().Length == 2)
                    .FirstOrDefault(x =>
                        x.GetParameters()[1].ParameterType == typeof(CancellationToken)
                        && x.Module.Name.Contains("Effect")
                    );

                if (method == null)
                    throw new TrainException(
                        $"Failed to find Run(input, ct) method for train type ({type.Name})"
                    );

                return method;
            }
        );

        var taskRunMethod = (Task<TOut>?)
            runWithCtMethod.Invoke(trainService, [trainInput, cancellationToken]);

        if (taskRunMethod is null)
            throw new TrainException(
                $"Failed to invoke Run(input, ct) method for train type ({trainType.Name})"
            );

        return await taskRunMethod;
    }

    public async Task RunAsync(object trainInput, Metadata? metadata = null)
    {
        using var scope = scopeFactory.CreateScope();
        var trainService = InitializeTrainFromProvider(scope.ServiceProvider, trainInput);
        var trainType = trainService.GetType();

        if (metadata != null)
        {
            if (metadata.TrainState != TrainState.Pending)
                throw new TrainException(
                    $"TrainBus will not run a passed Metadata with state ({metadata.TrainState}), Must be Pending"
                );

            var method = RunWithMetadataMethodCache.GetOrAdd(
                trainType,
                type =>
                {
                    var m = type.GetMethods()
                        .Where(x => x.Name == "Run")
                        .Where(x => x.GetParameters().Length == 2)
                        .FirstOrDefault(x =>
                            x.GetParameters()[1].ParameterType == typeof(Metadata)
                        );

                    if (m == null)
                        throw new TrainException(
                            $"Failed to find Run(input, metadata) method for train type ({type.Name})"
                        );

                    return m;
                }
            );

            var task =
                (Task?)method.Invoke(trainService, [trainInput, metadata])
                ?? throw new TrainException(
                    $"Failed to invoke Run(input, metadata) method for train type ({trainType.Name})"
                );

            await task;
            return;
        }

        var runMethod = RunMethodCache.GetOrAdd(
            trainType,
            type =>
            {
                var m = type.GetMethods()
                    .Where(x => x.Name == "Run")
                    .Where(x =>
                    {
                        var parameters = x.GetParameters();
                        return parameters.Length == 2
                            && parameters[1].ParameterType == typeof(CancellationToken);
                    })
                    .FirstOrDefault(x => x.Module.Name.Contains("Effect"));

                if (m == null)
                    throw new TrainException(
                        $"Failed to find Run method for train type ({type.Name})"
                    );

                return m;
            }
        );

        var taskRun =
            (Task?)runMethod.Invoke(trainService, [trainInput, CancellationToken.None])
            ?? throw new TrainException(
                $"Failed to invoke Run method for train type ({trainService.GetType().Name})"
            );

        await taskRun;
    }

    public async Task RunAsync(
        object trainInput,
        CancellationToken cancellationToken,
        Metadata? metadata = null
    )
    {
        using var scope = scopeFactory.CreateScope();
        var trainService = InitializeTrainFromProvider(scope.ServiceProvider, trainInput);
        var trainType = trainService.GetType();

        if (metadata != null)
        {
            if (metadata.TrainState != TrainState.Pending)
                throw new TrainException(
                    $"TrainBus will not run a passed Metadata with state ({metadata.TrainState}), Must be Pending"
                );

            var method = RunWithMetadataCtMethodCache.GetOrAdd(
                trainType,
                type =>
                {
                    var m = type.GetMethods()
                        .Where(x => x.Name == "Run")
                        .Where(x => x.GetParameters().Length == 3)
                        .FirstOrDefault(x =>
                            x.GetParameters()[1].ParameterType == typeof(Metadata)
                            && x.GetParameters()[2].ParameterType == typeof(CancellationToken)
                        );

                    if (m == null)
                        throw new TrainException(
                            $"Failed to find Run(input, metadata, ct) method for train type ({type.Name})"
                        );

                    return m;
                }
            );

            var task =
                (Task?)method.Invoke(trainService, [trainInput, metadata, cancellationToken])
                ?? throw new TrainException(
                    $"Failed to invoke Run(input, metadata, ct) method for train type ({trainType.Name})"
                );

            await task;
            return;
        }

        var runMethod = RunWithCtMethodCache.GetOrAdd(
            trainType,
            type =>
            {
                var m = type.GetMethods()
                    .Where(x => x.Name == "Run")
                    .Where(x => x.GetParameters().Length == 2)
                    .FirstOrDefault(x =>
                        x.GetParameters()[1].ParameterType == typeof(CancellationToken)
                        && x.Module.Name.Contains("Effect")
                    );

                if (m == null)
                    throw new TrainException(
                        $"Failed to find Run(input, ct) method for train type ({type.Name})"
                    );

                return m;
            }
        );

        var taskRun =
            (Task?)runMethod.Invoke(trainService, [trainInput, cancellationToken])
            ?? throw new TrainException(
                $"Failed to invoke Run(input, ct) method for train type ({trainType.Name})"
            );

        await taskRun;
    }
}
