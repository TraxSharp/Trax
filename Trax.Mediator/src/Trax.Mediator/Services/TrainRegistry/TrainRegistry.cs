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
internal class TrainRegistry : ITrainRegistry
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
    /// The discovered (serviceType, implementationType) pairs from assembly scanning.
    /// Reused by <c>RegisterServiceTrains</c> to avoid a second assembly scan.
    /// </summary>
    internal IReadOnlyList<(Type ServiceType, Type ImplementationType)> DiscoveredTrains { get; }

    /// <summary>
    /// The assemblies this registry scanned, each once, in the order given. Named in the error a
    /// host gets when no train takes an input, so it can see where Trax looked.
    /// </summary>
    internal IReadOnlyList<Assembly> ScannedAssemblies { get; }

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
    /// 3. Selects each train's service type: its own interface deriving from
    ///    IServiceTrain&lt;TIn, TOut&gt;, else the closed IServiceTrain&lt;TIn, TOut&gt;
    /// 4. Extracts the input types from the train interfaces
    /// 5. Builds a dictionary that maps input types to train types
    ///
    /// If a train implementation is found that does not properly implement the
    /// IServiceTrain&lt;TIn, TOut&gt; interface, a TrainException is thrown
    /// with detailed information about the invalid implementation.
    /// </remarks>
    public TrainRegistry(params Assembly[] assemblies)
    {
        ScannedAssemblies = [.. assemblies.Distinct()];

        // The type we will be looking for in our assemblies
        var trainType = typeof(IServiceTrain<,>);

        // Single scan: capture both the service type (interface) and the concrete type.
        // A HashSet keyed by concrete type prevents duplicates when the same assembly is passed twice.
        var seen = new HashSet<Type>();
        var discovered = new List<(Type ServiceType, Type ImplementationType)>();

        foreach (var assembly in assemblies)
        {
            var concreteTypes = assembly
                .GetTypes()
                .Where(x => x.IsClass)
                .Where(x => x.IsAbstract == false)
                .Where(x =>
                    x.GetInterfaces()
                        .Where(y => y.IsGenericType)
                        .Select(y => y.GetGenericTypeDefinition())
                        .Contains(trainType)
                );

            foreach (var concreteType in concreteTypes)
            {
                if (!seen.Add(concreteType))
                    continue;

                // The train's own interface (IMyTrain), or the closed IServiceTrain<TIn, TOut>
                // when it has none. Never the concrete class itself, which would give DI
                // disposal issues.
                var serviceType = TrainServiceType.Select(concreteType);

                discovered.Add((serviceType, concreteType));
            }
        }

        DiscoveredTrains = discovered;

        // Build the input type → train mapping, skipping duplicates.
        // Multiple trains can share the same input type (e.g., internal scheduler
        // trains using Unit). The bus's input-keyed RunAsync reaches only the first of
        // them; everything that names a train runs it by name instead (ITrainBus.RunByNameAsync).
        InputTypeToTrain = new Dictionary<Type, Type>();
        foreach (var (serviceType, implementationType) in discovered)
        {
            // The input type of the service type the train is registered under, which is the
            // one the bus resolves.
            var inputType =
                TrainServiceType.FindClosedServiceTrain(serviceType)?.GetGenericArguments()[0]
                ?? throw new TrainException(
                    $"Could not find an interface and/or an inherited interface of type ({trainType.Name}) on target type ({implementationType.Name}) with FullName ({implementationType.FullName}) on Assembly ({implementationType.AssemblyQualifiedName})."
                );

            InputTypeToTrain.TryAdd(inputType, serviceType);
        }
    }
}
