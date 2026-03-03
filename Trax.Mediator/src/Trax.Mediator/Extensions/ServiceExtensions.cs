using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Exceptions;
using Trax.Effect.Configuration.TraxEffectBuilder;
using Trax.Effect.Extensions;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Services.TrainBus;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Mediator.Services.TrainRegistry;

namespace Trax.Mediator.Extensions;

/// <summary>
/// Provides extension methods for configuring Trax.Mediator services in the dependency injection container.
/// </summary>
/// <remarks>
/// The ServiceExtensions class contains utility methods that simplify the registration
/// of Trax.Mediator services with the dependency injection system.
///
/// These extensions enable:
/// 1. Automatic train discovery and registration
/// 2. Configuration of the train bus and registry
/// 3. Integration with the Trax.Effect configuration system
///
/// By using these extensions, applications can easily configure and use the
/// Trax.Mediator system with minimal boilerplate code.
/// </remarks>
public static class ServiceExtensions
{
    /// <summary>
    /// Registers all effect trains found in the specified assemblies with the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection to add the trains to</param>
    /// <param name="serviceLifetime">The service lifetime to use for the trains</param>
    /// <param name="assemblies">The assemblies to scan for train implementations</param>
    /// <returns>The service collection for method chaining</returns>
    /// <exception cref="TrainException">
    /// Thrown when a train implementation is found that does not have at least one interface.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when an invalid service lifetime is specified.
    /// </exception>
    /// <remarks>
    /// This method scans the specified assemblies for classes that implement the
    /// IServiceTrain&lt;TIn, TOut&gt; interface and registers them with the
    /// dependency injection container.
    ///
    /// The method performs the following steps:
    /// 1. Identifies the IServiceTrain&lt;TIn, TOut&gt; generic type definition
    /// 2. Scans each assembly for classes that implement this interface
    /// 3. Extracts the train types and their interfaces
    /// 4. Registers each train with the dependency injection container
    ///
    /// Trains are registered with the specified service lifetime, which defaults
    /// to transient. This means that a new instance of the train is created each
    /// time it is requested from the container.
    ///
    /// Example usage:
    /// ```csharp
    /// services.RegisterServiceTrains(
    ///     ServiceLifetime.Scoped,
    ///     typeof(MyTrain).Assembly
    /// );
    /// ```
    /// </remarks>
    public static IServiceCollection RegisterServiceTrains(
        this IServiceCollection services,
        ServiceLifetime serviceLifetime = ServiceLifetime.Transient,
        params Assembly[] assemblies
    )
    {
        var trainType = typeof(IServiceTrain<,>);

        var types = new List<(Type, Type)>();
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
                .Select(type =>
                    (
                        type.GetInterfaces()
                            .FirstOrDefault(y => !y.IsGenericType && y != typeof(IDisposable))
                            ?? type.GetInterfaces().FirstOrDefault()
                            ?? throw new TrainException(
                                $"Could not find an interface attached to ({type.Name}) with Full Name ({type.FullName}) on Assembly ({type.AssemblyQualifiedName}). At least one Interface is required."
                            ),
                        type
                    )
                );

            types.AddRange(trainTypes);
        }

        foreach (var (typeInterface, typeImplementation) in types)
        {
            switch (serviceLifetime)
            {
                case ServiceLifetime.Singleton:
                    services.AddSingletonTraxRoute(typeInterface, typeImplementation);
                    break;
                case ServiceLifetime.Scoped:
                    services.AddScopedTraxRoute(typeInterface, typeImplementation);
                    break;
                case ServiceLifetime.Transient:
                    services.AddTransientTraxRoute(typeInterface, typeImplementation);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(serviceLifetime),
                        serviceLifetime,
                        null
                    );
            }
        }

        return services;
    }

    /// <summary>
    /// Adds the effect train bus and registry to the Trax.Core effect configuration builder.
    /// </summary>
    /// <param name="configurationBuilder">The Trax.Core effect configuration builder</param>
    /// <param name="serviceTrainLifetime">The service lifetime to use for the trains</param>
    /// <param name="assemblies">The assemblies to scan for train implementations</param>
    /// <returns>The configuration builder for method chaining</returns>
    /// <remarks>
    /// This method configures the Trax.Effect system to use the train bus and registry
    /// for executing trains. It adds the necessary services to the dependency injection
    /// container and configures them to scan the specified assemblies for train implementations.
    ///
    /// This method is typically used in the ConfigureServices method of the Startup class
    /// when configuring the Trax.Effect system.
    ///
    /// Example usage:
    /// ```csharp
    /// services.AddTraxEffects(options =>
    ///     options.AddServiceTrainBus(
    ///         ServiceLifetime.Scoped,
    ///         typeof(MyTrain).Assembly
    ///     )
    /// );
    /// ```
    /// </remarks>
    public static TraxEffectConfigurationBuilder AddServiceTrainBus(
        this TraxEffectConfigurationBuilder configurationBuilder,
        ServiceLifetime serviceTrainLifetime = ServiceLifetime.Transient,
        params Assembly[] assemblies
    )
    {
        configurationBuilder.ServiceCollection.AddServiceTrainBus(serviceTrainLifetime, assemblies);

        return configurationBuilder;
    }

    /// <summary>
    /// Adds the effect train bus and registry to the service collection.
    /// </summary>
    /// <param name="serviceCollection">The service collection to add the services to</param>
    /// <param name="serviceTrainLifetime">The service lifetime to use for the trains</param>
    /// <param name="assemblies">The assemblies to scan for train implementations</param>
    /// <returns>The service collection for method chaining</returns>
    /// <remarks>
    /// This method adds the train bus and registry to the dependency injection container
    /// and configures them to scan the specified assemblies for train implementations.
    ///
    /// The method performs the following steps:
    /// 1. Creates a new train registry that scans the specified assemblies
    /// 2. Registers the registry as a singleton in the container
    /// 3. Registers the train bus as a scoped service in the container
    /// 4. Registers all trains found in the assemblies with the container
    ///
    /// This method is typically used in the ConfigureServices method of the Startup class
    /// when configuring the dependency injection container.
    ///
    /// Example usage:
    /// ```csharp
    /// services.AddServiceTrainBus(
    ///     ServiceLifetime.Scoped,
    ///     typeof(MyTrain).Assembly
    /// );
    /// ```
    /// </remarks>
    public static IServiceCollection AddServiceTrainBus(
        this IServiceCollection serviceCollection,
        ServiceLifetime serviceTrainLifetime = ServiceLifetime.Transient,
        params Assembly[] assemblies
    )
    {
        var trainRegistry = new TrainRegistry(assemblies);

        return serviceCollection
            .AddSingleton<IServiceCollection>(serviceCollection)
            .AddSingleton<ITrainRegistry>(trainRegistry)
            .AddSingleton<ITrainDiscoveryService, TrainDiscoveryService>()
            .AddScoped<ITrainBus, TrainBus>()
            .AddScoped<ITrainExecutionService, TrainExecutionService>()
            .RegisterServiceTrains(serviceTrainLifetime, assemblies);
    }
}
