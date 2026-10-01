using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
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
using Trax.Mediator.Exceptions;
using Trax.Mediator.Services.TrainRegistry;
using ScanningTrainRegistry = Trax.Mediator.Services.TrainRegistry.TrainRegistry;

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
/// executes it, and disposes the scope asynchronously when done, so a scoped dependency that is
/// only <see cref="IAsyncDisposable"/> is released without replacing the run's outcome with a
/// disposal error. This ensures each train execution is fully
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
internal partial class TrainBus(
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

    /// <inheritdoc/>
    /// <remarks>
    /// Unlike <c>RunAsync</c>, the train is resolved from the bus's own scope, not a child scope,
    /// so it and its scoped dependencies live as long as that scope does.
    /// </remarks>
    public object InitializeTrain(object trainInput)
    {
        return InitializeTrainFromProvider(serviceProvider, trainInput);
    }

    /// <summary>
    /// Throw helper for the unreachable "Run method not found on a ServiceTrain" path.
    /// </summary>
    /// <remarks>
    /// Each cache lookup below knows it's looking up a method that, by the type system,
    /// must exist on <see cref="ServiceTrain{TIn,TOut}"/>. The fallthrough is a defense
    /// against a misregistered train type. Excluded from coverage because it is genuinely
    /// unreachable through the public API.
    /// </remarks>
    [ExcludeFromCodeCoverage]
    private static T MissingMethod<T>(string variant, string typeName) =>
        throw new TrainException($"Failed to find {variant} method for train type ({typeName})");

    /// <summary>
    /// Throw helper for the unreachable "MethodInfo.Invoke returned null" path.
    /// </summary>
    /// <remarks>
    /// The cached methods all return <c>Task</c> or <c>Task&lt;TOut&gt;</c>; CLR-level
    /// invocation will not return null for those signatures. Excluded from coverage as
    /// genuinely unreachable.
    /// </remarks>
    [ExcludeFromCodeCoverage]
    private static T InvocationNull<T>(string variant, string typeName) =>
        throw new TrainException($"Failed to invoke {variant} method for train type ({typeName})");

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
            throw new NoTrainForInputException(inputType, ScannedAssemblies(registry));

        var trainService = provider.GetRequiredService(correctTrain);
        provider.InjectProperties(trainService);

        return trainService;
    }

    /// <summary>The assemblies a scanning registry looked in, for the host's error message.</summary>
    private static IReadOnlyList<string> ScannedAssemblies(ITrainRegistry registry) =>
        registry is ScanningTrainRegistry scanning
            ? scanning.ScannedAssemblies.Select(a => a.GetName().Name ?? a.FullName ?? "").ToList()
            : [];

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
    /// <param name="metadata">
    /// A pre-created, <c>Pending</c> metadata record for the train to run as, or null for the train
    /// to create its own. It is not a parent link: any other state, including a running train's
    /// metadata, is refused with a TrainException.
    /// </param>
    /// <returns>A task that resolves to the train's output</returns>
    /// <exception cref="TrainException">
    /// Thrown when the input is null, the metadata passed is not <c>Pending</c>, the Run method
    /// cannot be found on the train, or the Run method invocation fails.
    /// </exception>
    /// <exception cref="NoTrainForInputException">No registered train takes the input's type.</exception>
    public async Task<TOut> RunAsync<TOut>(object trainInput, Metadata? metadata = null)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
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

                    return method ?? MissingMethod<MethodInfo>("Run(input, metadata)", type.Name);
                }
            );

            var taskWithMetadata =
                (Task<TOut>?)runWithMetadataMethod.Invoke(trainService, [trainInput, metadata])
                ?? InvocationNull<Task<TOut>>("Run(input, metadata)", trainType.Name);

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

                return method ?? MissingMethod<MethodInfo>("Run", type.Name);
            }
        );

        // And finally run the train, casting the return type to preserve type safety.
        var taskRunMethod =
            (Task<TOut>?)runMethod.Invoke(trainService, [trainInput, CancellationToken.None])
            ?? InvocationNull<Task<TOut>>("Run", trainService.GetType().Name);

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
        await using var scope = scopeFactory.CreateAsyncScope();
        var trainService = InitializeTrainFromProvider(scope.ServiceProvider, trainInput);

        return await RunResolvedAsync<TOut>(trainService, trainInput, metadata, cancellationToken);
    }

    /// <summary>
    /// Runs an already-resolved train through <c>Run(input, metadata, ct)</c> when
    /// <paramref name="metadata"/> is given, or <c>Run(input, ct)</c> when it is not.
    /// </summary>
    private static async Task<TOut> RunResolvedAsync<TOut>(
        object trainService,
        object trainInput,
        Metadata? metadata,
        CancellationToken cancellationToken
    )
    {
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

                    return method
                        ?? MissingMethod<MethodInfo>("Run(input, metadata, ct)", type.Name);
                }
            );

            var taskWithMetadata =
                (Task<TOut>?)
                    runWithMetadataCtMethod.Invoke(
                        trainService,
                        [trainInput, metadata, cancellationToken]
                    )
                ?? InvocationNull<Task<TOut>>("Run(input, metadata, ct)", trainType.Name);

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

                return method ?? MissingMethod<MethodInfo>("Run(input, ct)", type.Name);
            }
        );

        var taskRunMethod =
            (Task<TOut>?)runWithCtMethod.Invoke(trainService, [trainInput, cancellationToken])
            ?? InvocationNull<Task<TOut>>("Run(input, ct)", trainType.Name);

        return await taskRunMethod;
    }

    /// <summary>
    /// Runs the train registered for the input's runtime type and discards its output. Creates a
    /// child DI scope for the run and disposes it afterwards.
    /// </summary>
    /// <param name="trainInput">The input; its runtime type selects the train.</param>
    /// <param name="metadata">
    /// A pre-created, <c>Pending</c> metadata record for the train to run as, or null for the train
    /// to create its own.
    /// </param>
    /// <exception cref="TrainException">
    /// The input is null, no train is registered for its type, or <paramref name="metadata"/> is
    /// not <c>Pending</c>.
    /// </exception>
    public async Task RunAsync(object trainInput, Metadata? metadata = null)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
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

                    return m ?? MissingMethod<MethodInfo>("Run(input, metadata)", type.Name);
                }
            );

            var task =
                (Task?)method.Invoke(trainService, [trainInput, metadata])
                ?? InvocationNull<Task>("Run(input, metadata)", trainType.Name);

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

                return m ?? MissingMethod<MethodInfo>("Run", type.Name);
            }
        );

        var taskRun =
            (Task?)runMethod.Invoke(trainService, [trainInput, CancellationToken.None])
            ?? InvocationNull<Task>("Run", trainService.GetType().Name);

        await taskRun;
    }

    /// <summary>
    /// Runs the train registered for the input's runtime type with a cancellation token and
    /// discards its output. Creates a child DI scope for the run and disposes it afterwards.
    /// </summary>
    /// <param name="trainInput">The input; its runtime type selects the train.</param>
    /// <param name="cancellationToken">Passed to the train's <c>Run</c>.</param>
    /// <param name="metadata">
    /// A pre-created, <c>Pending</c> metadata record for the train to run as, or null for the train
    /// to create its own.
    /// </param>
    /// <exception cref="TrainException">
    /// The input is null, no train is registered for its type, or <paramref name="metadata"/> is
    /// not <c>Pending</c>.
    /// </exception>
    public async Task RunAsync(
        object trainInput,
        CancellationToken cancellationToken,
        Metadata? metadata = null
    )
    {
        await using var scope = scopeFactory.CreateAsyncScope();
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

                    return m ?? MissingMethod<MethodInfo>("Run(input, metadata, ct)", type.Name);
                }
            );

            var task =
                (Task?)method.Invoke(trainService, [trainInput, metadata, cancellationToken])
                ?? InvocationNull<Task>("Run(input, metadata, ct)", trainType.Name);

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

                return m ?? MissingMethod<MethodInfo>("Run(input, ct)", type.Name);
            }
        );

        var taskRun =
            (Task?)runMethod.Invoke(trainService, [trainInput, cancellationToken])
            ?? InvocationNull<Task>("Run(input, ct)", trainType.Name);

        await taskRun;
    }
}
