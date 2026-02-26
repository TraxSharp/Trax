using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Trax.Effect.Configuration.Trax.CoreEffectBuilder;
using Trax.Effect.Extensions;
using Trax.Mediator.Services.WorkflowBus;
using Trax.Mediator.Services.WorkflowRegistry;
using Trax.Effect.Services.ServiceTrain;
using Trax.Core.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.Mediator.Extensions;

/// <summary>
/// Provides extension methods for configuring Trax.Mediator services in the dependency injection container.
/// </summary>
/// <remarks>
/// The ServiceExtensions class contains utility methods that simplify the registration
/// of Trax.Mediator services with the dependency injection system.
///
/// These extensions enable:
/// 1. Automatic workflow discovery and registration
/// 2. Configuration of the workflow bus and registry
/// 3. Integration with the Trax.Effect configuration system
///
/// By using these extensions, applications can easily configure and use the
/// Trax.Mediator system with minimal boilerplate code.
/// </remarks>
public static class ServiceExtensions
{
    /// <summary>
    /// Registers all effect workflows found in the specified assemblies with the dependency injection container.
    /// </summary>
    /// <param name="services">The service collection to add the workflows to</param>
    /// <param name="serviceLifetime">The service lifetime to use for the workflows</param>
    /// <param name="assemblies">The assemblies to scan for workflow implementations</param>
    /// <returns>The service collection for method chaining</returns>
    /// <exception cref="WorkflowException">
    /// Thrown when a workflow implementation is found that does not have at least one interface.
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
    /// 3. Extracts the workflow types and their interfaces
    /// 4. Registers each workflow with the dependency injection container
    ///
    /// Workflows are registered with the specified service lifetime, which defaults
    /// to transient. This means that a new instance of the workflow is created each
    /// time it is requested from the container.
    ///
    /// Example usage:
    /// ```csharp
    /// services.RegisterServiceTrains(
    ///     ServiceLifetime.Scoped,
    ///     typeof(MyWorkflow).Assembly
    /// );
    /// ```
    /// </remarks>
    public static IServiceCollection RegisterServiceTrains(
        this IServiceCollection services,
        ServiceLifetime serviceLifetime = ServiceLifetime.Transient,
        params Assembly[] assemblies
    )
    {
        var workflowType = typeof(IServiceTrain<,>);

        var types = new List<(Type, Type)>();
        foreach (var assembly in assemblies)
        {
            var workflowTypes = assembly
                .GetTypes()
                .Where(x => x.IsClass)
                .Where(x => x.IsAbstract == false)
                .Where(
                    x =>
                        x.GetInterfaces()
                            .Where(y => y.IsGenericType)
                            .Select(y => y.GetGenericTypeDefinition())
                            .Contains(workflowType)
                )
                .Select(
                    type =>
                        (
                            type.GetInterfaces()
                                .FirstOrDefault(y => !y.IsGenericType && y != typeof(IDisposable))
                                ?? type.GetInterfaces().FirstOrDefault()
                                ?? throw new WorkflowException(
                                    $"Could not find an interface attached to ({type.Name}) with Full Name ({type.FullName}) on Assembly ({type.AssemblyQualifiedName}). At least one Interface is required."
                                ),
                            type
                        )
                );

            types.AddRange(workflowTypes);
        }

        foreach (var (typeInterface, typeImplementation) in types)
        {
            switch (serviceLifetime)
            {
                case ServiceLifetime.Singleton:
                    services.AddSingletonTrax.CoreRoute(typeInterface, typeImplementation);
                    break;
                case ServiceLifetime.Scoped:
                    services.AddScopedTrax.CoreRoute(typeInterface, typeImplementation);
                    break;
                case ServiceLifetime.Transient:
                    services.AddTransientTrax.CoreRoute(typeInterface, typeImplementation);
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
    /// Adds the effect workflow bus and registry to the Trax.Core effect configuration builder.
    /// </summary>
    /// <param name="configurationBuilder">The Trax.Core effect configuration builder</param>
    /// <param name="serviceTrainLifetime">The service lifetime to use for the workflows</param>
    /// <param name="assemblies">The assemblies to scan for workflow implementations</param>
    /// <returns>The configuration builder for method chaining</returns>
    /// <remarks>
    /// This method configures the Trax.Effect system to use the workflow bus and registry
    /// for executing workflows. It adds the necessary services to the dependency injection
    /// container and configures them to scan the specified assemblies for workflow implementations.
    ///
    /// This method is typically used in the ConfigureServices method of the Startup class
    /// when configuring the Trax.Effect system.
    ///
    /// Example usage:
    /// ```csharp
    /// services.AddTrax.CoreEffects(options =>
    ///     options.AddServiceTrainBus(
    ///         ServiceLifetime.Scoped,
    ///         typeof(MyWorkflow).Assembly
    ///     )
    /// );
    /// ```
    /// </remarks>
    public static Trax.CoreEffectConfigurationBuilder AddServiceTrainBus(
        this Trax.CoreEffectConfigurationBuilder configurationBuilder,
        ServiceLifetime serviceTrainLifetime = ServiceLifetime.Transient,
        params Assembly[] assemblies
    )
    {
        configurationBuilder.ServiceCollection.AddServiceTrainBus(serviceTrainLifetime, assemblies);

        return configurationBuilder;
    }

    /// <summary>
    /// Adds the effect workflow bus and registry to the service collection.
    /// </summary>
    /// <param name="serviceCollection">The service collection to add the services to</param>
    /// <param name="serviceTrainLifetime">The service lifetime to use for the workflows</param>
    /// <param name="assemblies">The assemblies to scan for workflow implementations</param>
    /// <returns>The service collection for method chaining</returns>
    /// <remarks>
    /// This method adds the workflow bus and registry to the dependency injection container
    /// and configures them to scan the specified assemblies for workflow implementations.
    ///
    /// The method performs the following steps:
    /// 1. Creates a new workflow registry that scans the specified assemblies
    /// 2. Registers the registry as a singleton in the container
    /// 3. Registers the workflow bus as a scoped service in the container
    /// 4. Registers all workflows found in the assemblies with the container
    ///
    /// This method is typically used in the ConfigureServices method of the Startup class
    /// when configuring the dependency injection container.
    ///
    /// Example usage:
    /// ```csharp
    /// services.AddServiceTrainBus(
    ///     ServiceLifetime.Scoped,
    ///     typeof(MyWorkflow).Assembly
    /// );
    /// ```
    /// </remarks>
    public static IServiceCollection AddServiceTrainBus(
        this IServiceCollection serviceCollection,
        ServiceLifetime serviceTrainLifetime = ServiceLifetime.Transient,
        params Assembly[] assemblies
    )
    {
        var workflowRegistry = new WorkflowRegistry(assemblies);

        return serviceCollection
            .AddSingleton<IWorkflowRegistry>(workflowRegistry)
            .AddScoped<IWorkflowBus, WorkflowBus>()
            .RegisterServiceTrains(serviceTrainLifetime, assemblies);
    }
}
