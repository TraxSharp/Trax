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
using Trax.Mediator.Services.WorkflowRegistry;

namespace Trax.Mediator.Services.WorkflowBus;

/// <summary>
/// Implements the workflow bus that dynamically executes workflows based on their input type.
/// </summary>
/// <remarks>
/// The WorkflowBus class provides the core implementation of the mediator pattern for workflows.
/// It uses reflection and dependency injection to dynamically discover, instantiate, and execute
/// the appropriate workflow for a given input type.
///
/// The workflow bus relies on the workflow registry to map input types to workflow types,
/// and uses the service provider to resolve and instantiate the workflow instances.
///
/// This implementation supports:
/// - Dynamic workflow discovery based on input type
/// - Automatic dependency injection for workflow instances
/// - Property injection for workflows
/// - Metadata association for tracking and logging
/// - Type-safe execution with generic output types
///
/// The workflow bus is typically registered as a scoped service in the dependency injection
/// container, allowing it to be injected into controllers, services, or other components
/// that need to execute workflows.
/// </remarks>
/// <param name="serviceProvider">The service provider used to resolve workflow instances</param>
/// <param name="registryService">The registry service that maps input types to workflow types</param>
public class WorkflowBus(IServiceProvider serviceProvider, IWorkflowRegistry registryService)
    : IWorkflowBus
{
    /// <summary>
    /// Thread-safe cache for storing reflection method lookups to improve performance.
    /// </summary>
    /// <remarks>
    /// This cache stores MethodInfo objects keyed by workflow type to avoid repeated reflection operations.
    /// Using ConcurrentDictionary ensures thread-safety for scoped service usage.
    /// </remarks>
    private static readonly ConcurrentDictionary<Type, MethodInfo> RunMethodCache = new();

    /// <summary>
    /// Thread-safe cache for storing the 2-parameter Run(TIn, Metadata) method lookups.
    /// </summary>
    /// <remarks>
    /// This cache is used when executing workflows with pre-created metadata (e.g., from the scheduler).
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
    /// This method is primarily intended for testing scenarios where multiple workflow types
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

    public object InitializeWorkflow(object workflowInput)
    {
        if (workflowInput == null)
            throw new WorkflowException(
                "workflowInput is null as input to WorkflowBus.SendAsync(...)"
            );

        // The full type of the input, rather than just the interface
        var inputType = workflowInput.GetType();

        var foundWorkflow = registryService.InputTypeToWorkflow.TryGetValue(
            inputType,
            out var correctWorkflow
        );

        if (foundWorkflow == false || correctWorkflow == null)
            throw new WorkflowException(
                $"Could not find workflow with input type ({inputType.Name})"
            );

        var workflowService = serviceProvider.GetRequiredService(correctWorkflow);
        serviceProvider.InjectProperties(workflowService);

        return workflowService;
    }

    /// <summary>
    /// Executes a workflow that accepts the specified input type and returns the specified output type.
    /// </summary>
    /// <typeparam name="TOut">The expected output type of the workflow</typeparam>
    /// <param name="workflowInput">The input object for the workflow</param>
    /// <param name="metadata">Optional metadata to associate with the workflow execution</param>
    /// <returns>A task that resolves to the workflow's output</returns>
    /// <exception cref="WorkflowException">
    /// Thrown when the input is null, no workflow is found for the input type,
    /// the Run method cannot be found on the workflow, or the Run method invocation fails.
    /// </exception>
    /// <remarks>
    /// This method performs the following steps:
    /// 1. Validates that the input is not null
    /// 2. Gets the type of the input object
    /// 3. Looks up the workflow type in the registry based on the input type
    /// 4. Resolves the workflow instance from the service provider
    /// 5. Injects properties into the workflow instance
    /// 6. Sets the parent ID if metadata is provided (for parent-child relationships)
    /// 7. Finds the appropriate Run method on the workflow using reflection
    /// 8. Invokes the Run method with the input (and metadata if scheduler-created)
    /// 9. Returns the result cast to the expected output type
    ///
    /// The method uses reflection to find and invoke the Run method because workflows
    /// can have multiple Run method implementations, and we need to select the correct one
    /// from Trax.Effect rather than the base Trax.Core implementation.
    ///
    /// When metadata with a ManifestId is provided (scheduler-created metadata), the
    /// Run(TIn, Metadata) overload is called so the workflow uses that pre-created metadata
    /// instead of creating a new one. This ensures the scheduler's metadata record is properly
    /// updated from Pending to Completed.
    /// </remarks>
    public Task<TOut> RunAsync<TOut>(object workflowInput, Metadata? metadata = null)
    {
        var workflowService = InitializeWorkflow(workflowInput);
        var workflowType = workflowService.GetType();

        // When metadata is pre-created and Pending (from the scheduler or ad-hoc dashboard execution),
        // use the 2-param Run(input, metadata) method so the workflow uses this
        // pre-created metadata instead of creating a new one.
        if (metadata != null)
        {
            if (metadata.WorkflowState != WorkflowState.Pending)
                throw new WorkflowException(
                    $"WorkflowBus will not run a passed Metadata with state ({metadata.WorkflowState}), Must be Pending"
                );

            var runWithMetadataMethod = RunWithMetadataMethodCache.GetOrAdd(
                workflowType,
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
                        throw new WorkflowException(
                            $"Failed to find Run(input, metadata) method for workflow type ({type.Name})"
                        );

                    return method;
                }
            );

            var taskWithMetadata = (Task<TOut>?)
                runWithMetadataMethod.Invoke(workflowService, [workflowInput, metadata]);

            if (taskWithMetadata is null)
                throw new WorkflowException(
                    $"Failed to invoke Run(input, metadata) method for workflow type ({workflowType.Name})"
                );

            return taskWithMetadata;
        }

        // Standard case: Get the 1-param Run method from cache
        var runMethod = RunMethodCache.GetOrAdd(
            workflowType,
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
                    throw new WorkflowException(
                        $"Failed to find Run method for workflow type ({type.Name})"
                    );

                return method;
            }
        );

        // And finally run the workflow, casting the return type to preserve type safety.
        var taskRunMethod = (Task<TOut>?)
            runMethod.Invoke(workflowService, [workflowInput, CancellationToken.None]);

        if (taskRunMethod is null)
            throw new WorkflowException(
                $"Failed to invoke Run method for workflow type ({workflowService.GetType().Name})"
            );

        return taskRunMethod;
    }

    /// <summary>
    /// Executes a workflow with cancellation support.
    /// </summary>
    public Task<TOut> RunAsync<TOut>(
        object workflowInput,
        CancellationToken cancellationToken,
        Metadata? metadata = null
    )
    {
        var workflowService = InitializeWorkflow(workflowInput);
        var workflowType = workflowService.GetType();

        if (metadata != null)
        {
            if (metadata.WorkflowState != WorkflowState.Pending)
                throw new WorkflowException(
                    $"WorkflowBus will not run a passed Metadata with state ({metadata.WorkflowState}), Must be Pending"
                );

            // Find Run(input, metadata, ct) — 3 params
            var runWithMetadataCtMethod = RunWithMetadataCtMethodCache.GetOrAdd(
                workflowType,
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
                        throw new WorkflowException(
                            $"Failed to find Run(input, metadata, ct) method for workflow type ({type.Name})"
                        );

                    return method;
                }
            );

            var taskWithMetadata = (Task<TOut>?)
                runWithMetadataCtMethod.Invoke(
                    workflowService,
                    [workflowInput, metadata, cancellationToken]
                );

            if (taskWithMetadata is null)
                throw new WorkflowException(
                    $"Failed to invoke Run(input, metadata, ct) method for workflow type ({workflowType.Name})"
                );

            return taskWithMetadata;
        }

        // Find Run(input, ct) — 2 params where second is CancellationToken
        var runWithCtMethod = RunWithCtMethodCache.GetOrAdd(
            workflowType,
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
                    throw new WorkflowException(
                        $"Failed to find Run(input, ct) method for workflow type ({type.Name})"
                    );

                return method;
            }
        );

        var taskRunMethod = (Task<TOut>?)
            runWithCtMethod.Invoke(workflowService, [workflowInput, cancellationToken]);

        if (taskRunMethod is null)
            throw new WorkflowException(
                $"Failed to invoke Run(input, ct) method for workflow type ({workflowType.Name})"
            );

        return taskRunMethod;
    }

    public async Task RunAsync(object workflowInput, Metadata? metadata = null)
    {
        await RunAsync<Unit>(workflowInput, metadata);
    }

    public async Task RunAsync(
        object workflowInput,
        CancellationToken cancellationToken,
        Metadata? metadata = null
    )
    {
        await RunAsync<Unit>(workflowInput, cancellationToken, metadata);
    }
}
