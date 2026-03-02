using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Trax.Core.Exceptions;
using Trax.Effect.Services.ServiceTrain;

namespace Trax.Mediator.Services.TrainRegistry;

/// <summary>
/// Implements a registry that maps train input types to their corresponding train types.
/// </summary>
/// <remarks>
/// The TrainRegistry class provides the core implementation of the train registry.
/// It scans the provided assemblies for train implementations and builds a dictionary
/// that maps input types to train types.
///
/// The registry uses reflection to discover train implementations in the provided assemblies.
/// It looks for classes that implement the IServiceTrain&lt;TIn, TOut&gt; interface and
/// extracts their input types to build the mapping.
///
/// This implementation supports:
/// - Automatic train discovery via assembly scanning
/// - Interface-based train registration (preferring interfaces over concrete types)
/// - Comprehensive error reporting for invalid train implementations
///
/// The registry is typically created during application startup and registered as a singleton
/// in the dependency injection container, allowing it to be injected into the train bus.
/// </remarks>
public class TrainRegistry : ITrainRegistry
{
    /// <summary>
    /// Gets or sets the dictionary that maps train input types to their corresponding train types.
    /// </summary>
    /// <remarks>
    /// This dictionary is populated during construction by scanning the provided assemblies
    /// for train implementations and extracting their input types.
    /// </remarks>
    public Dictionary<Type, Type> InputTypeToTrain { get; set; }

    /// <summary>
    /// Initializes a new instance of the TrainRegistry class by scanning the provided assemblies for train implementations.
    /// </summary>
    /// <param name="assemblies">The assemblies to scan for train implementations</param>
    /// <remarks>
    /// This constructor scans the provided assemblies for classes that implement the
    /// IServiceTrain&lt;TIn, TOut&gt; interface and builds a dictionary that maps
    /// input types to train types.
    ///
    /// The constructor performs the following steps:
    /// 1. Identifies the IServiceTrain&lt;TIn, TOut&gt; generic type definition
    /// 2. Scans each assembly for classes that implement this interface
    /// 3. Extracts the train types, preferring interfaces over concrete types
    /// 4. Extracts the input types from the train interfaces
    /// 5. Builds a dictionary that maps input types to train types
    ///
    /// If a train implementation is found that does not properly implement the
    /// IServiceTrain&lt;TIn, TOut&gt; interface, a TrainException is thrown
    /// with detailed information about the invalid implementation.
    /// </remarks>
    public TrainRegistry(params Assembly[] assemblies)
    {
        // The type we will be looking for in our assemblies
        var trainType = typeof(IServiceTrain<,>);

        var allTrainTypes = new HashSet<Type>();

        foreach (var assembly in assemblies)
        {
            var trainTypes = assembly
                .GetTypes()
                .Where(x => x.IsClass)
                .Where(x => x.IsAbstract == false)
                .Where(x =>
                    x.GetInterfaces()
                        .Where(y => y.IsGenericType)
                        .Select(y => y.GetGenericTypeDefinition())
                        .Contains(trainType)
                )
                .Select(x =>
                    // Prefer to inject via interface, but if it doesn't exist then inject by underlying type
                    x.GetInterfaces()
                        .FirstOrDefault(y => !y.IsGenericType && y != typeof(IDisposable))
                    ?? x
                );

            allTrainTypes.UnionWith(trainTypes);
        }

        // Build the input type → train mapping, skipping duplicates.
        // Multiple trains can share the same input type (e.g., internal scheduler
        // trains using Unit). These are resolved directly from DI rather than the bus.
        InputTypeToTrain = new Dictionary<Type, Type>();
        foreach (var wf in allTrainTypes)
        {
            var inputType =
                wf.GetInterfaces()
                    .Where(interfaceType => interfaceType.IsGenericType)
                    .FirstOrDefault(interfaceType =>
                        interfaceType.GetGenericTypeDefinition() == trainType
                    )
                    ?.GetGenericArguments()
                    .FirstOrDefault()
                ?? throw new TrainException(
                    $"Could not find an interface and/or an inherited interface of type ({trainType.Name}) on target type ({wf.Name}) with FullName ({wf.FullName}) on Assembly ({wf.AssemblyQualifiedName})."
                );

            InputTypeToTrain.TryAdd(inputType, wf);
        }
    }
}
