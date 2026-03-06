using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Trax.Core.Exceptions;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Extensions;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Configuration;
using Trax.Mediator.Services.TrainBus;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Mediator.Services.TrainRegistry;

namespace Trax.Mediator.Extensions;

/// <summary>
/// Provides extension methods for configuring Trax.Mediator services in the dependency injection container.
/// </summary>
public static class ServiceExtensions
{
    /// <summary>
    /// Registers all effect trains found in the specified assemblies with the dependency injection container.
    /// </summary>
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
    /// Adds the Trax mediator system (train bus, registry, discovery, and assembly scanning).
    /// </summary>
    /// <param name="builder">The builder after effects have been configured</param>
    /// <param name="configure">Action to configure the mediator builder</param>
    /// <returns>A <see cref="TraxBuilderWithMediator"/> that enables chaining <c>AddScheduler()</c>.</returns>
    public static TraxBuilderWithMediator AddMediator(
        this TraxBuilderWithEffects builder,
        Action<TraxMediatorBuilder> configure
    )
    {
        var mediatorBuilder = new TraxMediatorBuilder(builder);
        configure(mediatorBuilder);
        mediatorBuilder.Build();
        return new TraxBuilderWithMediator(builder);
    }

    /// <summary>
    /// Adds the Trax mediator system, scanning the specified assemblies for train implementations.
    /// </summary>
    /// <param name="builder">The builder after effects have been configured</param>
    /// <param name="assemblies">Assemblies to scan for IServiceTrain implementations</param>
    /// <returns>A <see cref="TraxBuilderWithMediator"/> that enables chaining <c>AddScheduler()</c>.</returns>
    public static TraxBuilderWithMediator AddMediator(
        this TraxBuilderWithEffects builder,
        params Assembly[] assemblies
    )
    {
        builder.ServiceCollection.AddServiceTrainBus(ServiceLifetime.Transient, assemblies);
        return new TraxBuilderWithMediator(builder);
    }

    /// <summary>
    /// Adds the train bus and registry to the service collection.
    /// </summary>
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
