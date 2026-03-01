using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Trax.Core.Exceptions;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Services.WorkflowRegistry;

/// <summary>
/// Implements a registry that maps workflow input types to their corresponding workflow types.
/// </summary>
/// <remarks>
/// The WorkflowRegistry class provides the core implementation of the workflow registry.
/// It scans the provided assemblies for workflow implementations and builds a dictionary
/// that maps input types to workflow types.
///
/// The registry uses reflection to discover workflow implementations in the provided assemblies.
/// It looks for classes that implement the IServiceTrain&lt;TIn, TOut&gt; interface and
/// extracts their input types to build the mapping.
///
/// This implementation supports:
/// - Automatic workflow discovery via assembly scanning
/// - Interface-based workflow registration (preferring interfaces over concrete types)
/// - Comprehensive error reporting for invalid workflow implementations
///
/// The registry is typically created during application startup and registered as a singleton
/// in the dependency injection container, allowing it to be injected into the workflow bus.
/// </remarks>
public class WorkflowRegistry : IWorkflowRegistry
{
    /// <summary>
    /// Gets or sets the dictionary that maps workflow input types to their corresponding workflow types.
    /// </summary>
    /// <remarks>
    /// This dictionary is populated during construction by scanning the provided assemblies
    /// for workflow implementations and extracting their input types.
    /// </remarks>
    public Dictionary<Type, Type> InputTypeToWorkflow { get; set; }

    /// <summary>
    /// Initializes a new instance of the WorkflowRegistry class by scanning the provided assemblies for workflow implementations.
    /// </summary>
    /// <param name="assemblies">The assemblies to scan for workflow implementations</param>
    /// <remarks>
    /// This constructor scans the provided assemblies for classes that implement the
    /// IServiceTrain&lt;TIn, TOut&gt; interface and builds a dictionary that maps
    /// input types to workflow types.
    ///
    /// The constructor performs the following steps:
    /// 1. Identifies the IServiceTrain&lt;TIn, TOut&gt; generic type definition
    /// 2. Scans each assembly for classes that implement this interface
    /// 3. Extracts the workflow types, preferring interfaces over concrete types
    /// 4. Extracts the input types from the workflow interfaces
    /// 5. Builds a dictionary that maps input types to workflow types
    ///
    /// If a workflow implementation is found that does not properly implement the
    /// IServiceTrain&lt;TIn, TOut&gt; interface, a WorkflowException is thrown
    /// with detailed information about the invalid implementation.
    /// </remarks>
    public WorkflowRegistry(params Assembly[] assemblies)
    {
        // The type we will be looking for in our assemblies
        var workflowType = typeof(IServiceTrain<,>);

        var allWorkflowTypes = new HashSet<Type>();

        foreach (var assembly in assemblies)
        {
            var workflowTypes = assembly
                .GetTypes()
                .Where(x => x.IsClass)
                .Where(x => x.IsAbstract == false)
                .Where(x =>
                    x.GetInterfaces()
                        .Where(y => y.IsGenericType)
                        .Select(y => y.GetGenericTypeDefinition())
                        .Contains(workflowType)
                )
                .Select(x =>
                    // Prefer to inject via interface, but if it doesn't exist then inject by underlying type
                    x.GetInterfaces()
                        .FirstOrDefault(y => !y.IsGenericType && y != typeof(IDisposable))
                    ?? x
                );

            allWorkflowTypes.UnionWith(workflowTypes);
        }

        // Build the input type → workflow mapping, skipping duplicates.
        // Multiple workflows can share the same input type (e.g., internal scheduler
        // workflows using Unit). These are resolved directly from DI rather than the bus.
        InputTypeToWorkflow = new Dictionary<Type, Type>();
        foreach (var wf in allWorkflowTypes)
        {
            var inputType =
                wf.GetInterfaces()
                    .Where(interfaceType => interfaceType.IsGenericType)
                    .FirstOrDefault(interfaceType =>
                        interfaceType.GetGenericTypeDefinition() == workflowType
                    )
                    ?.GetGenericArguments()
                    .FirstOrDefault()
                ?? throw new WorkflowException(
                    $"Could not find an interface and/or an inherited interface of type ({workflowType.Name}) on target type ({wf.Name}) with FullName ({wf.FullName}) on Assembly ({wf.AssemblyQualifiedName})."
                );

            InputTypeToWorkflow.TryAdd(inputType, wf);
        }
    }
}
