using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Core.Exceptions;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Data.Services.EnqueueContext;
using Trax.Effect.Data.Services.WorkQueuePromotion;
using Trax.Effect.Extensions;
using Trax.Effect.Services.ServiceTrain;
using Trax.Mediator.Configuration;
using Trax.Mediator.Services.ConcurrencyLimiter;
using Trax.Mediator.Services.Principal;
using Trax.Mediator.Services.RunExecutor;
using Trax.Mediator.Services.TrainAuthorization;
using Trax.Mediator.Services.TrainBus;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrainExecution;
using Trax.Mediator.Services.TrainRegistry;
using Trax.Mediator.Services.TrustedExecution;

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
    /// <param name="configure">
    /// A function that configures the mediator builder. Return the builder from the last chained call.
    /// </param>
    /// <returns>A <see cref="TraxBuilderWithMediator"/> that enables chaining <c>AddScheduler()</c>.</returns>
    public static TraxBuilderWithMediator AddMediator(
        this TraxBuilderWithEffects builder,
        Func<TraxMediatorBuilder, TraxMediatorBuilder> configure
    )
    {
        var mediatorBuilder = new TraxMediatorBuilder(builder);
        configure(mediatorBuilder);

        // Merge assemblies contributed by earlier subsystems (e.g. AddStateMachines adds its generic
        // mutations' assembly) so the host never names them. The fluent chain runs those subsystems on
        // TraxBuilderWithEffects, before AddMediator, so the list is fully populated here.
        if (builder.Root.ContributedMediatorAssemblies.Count > 0)
            mediatorBuilder.ScanAssemblies([.. builder.Root.ContributedMediatorAssemblies]);
        builder.Root.MediatorConfigured = true;

        var configuration = mediatorBuilder.Build();

        builder.ServiceCollection.AddSingleton(configuration);
        builder.ServiceCollection.AddServiceTrainBus(
            configuration.TrainLifetime,
            configuration.Assemblies
        );

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
        return builder.AddMediator(mediator => mediator.ScanAssemblies(assemblies));
    }

    /// <summary>
    /// Registers a hosted service ahead of everything already in the collection, so it starts
    /// before any hosted service the host registered before calling into Trax.
    /// </summary>
    /// <remarks>
    /// .NET starts hosted services in registration order, and <c>AddMediator</c> runs wherever the
    /// host happens to call it. A worker registered first therefore began claiming and running work
    /// before the chain check had refused the host, and with
    /// <c>HostOptions.ServicesStartConcurrently</c> it ran alongside it: the host was then disposed
    /// under those runs, leaving their rows <c>InProgress</c> and holding their subjects.
    /// <para>
    /// Prepending settles it wherever <c>AddMediator</c> is called, which is why it is done rather
    /// than reading the collection to find out what is already there. A startup gate that only
    /// sometimes runs first is not a gate.
    /// </para>
    /// </remarks>
    private static IServiceCollection PrependHostedService<THostedService>(
        this IServiceCollection services
    )
        where THostedService : class, IHostedService
    {
        services.Insert(0, ServiceDescriptor.Singleton<IHostedService, THostedService>());

        return services;
    }

    /// <summary>
    /// Registers pre-scanned train types with the DI container. Used internally by
    /// <see cref="AddServiceTrainBus"/> to avoid a second assembly scan.
    /// </summary>
    internal static IServiceCollection RegisterServiceTrains(
        this IServiceCollection services,
        IReadOnlyList<(Type ServiceType, Type ImplementationType)> trains,
        ServiceLifetime serviceLifetime
    )
    {
        foreach (var (serviceType, implementationType) in trains)
        {
            switch (serviceLifetime)
            {
                case ServiceLifetime.Singleton:
                    services.AddSingletonTraxRoute(serviceType, implementationType);
                    break;
                case ServiceLifetime.Scoped:
                    services.AddScopedTraxRoute(serviceType, implementationType);
                    break;
                case ServiceLifetime.Transient:
                    services.AddTransientTraxRoute(serviceType, implementationType);
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
    /// Adds the train bus and registry to the service collection.
    /// </summary>
    public static IServiceCollection AddServiceTrainBus(
        this IServiceCollection serviceCollection,
        ServiceLifetime serviceTrainLifetime = ServiceLifetime.Transient,
        params Assembly[] assemblies
    )
    {
        var trainRegistry = new TrainRegistry(assemblies);

        // Prepended, not appended, so they run before any hosted service the host registered before
        // it called into Trax. Reverse order, because each goes to the front: the chain check ends
        // up first.
        serviceCollection.PrependHostedService<AuthorizationRegistrationValidator>();
        serviceCollection.PrependHostedService<Services.ChainVerification.TrainChainStartupValidator>();

        return serviceCollection
            .AddSingleton<IServiceCollection>(serviceCollection)
            .AddSingleton<ITrainRegistry>(trainRegistry)
            .AddSingleton<ITrainDiscoveryService, TrainDiscoveryService>()
            .AddSingleton<IConcurrencyLimiter, ConcurrencyLimiter>()
            .AddSingleton<ITrustedExecutionScope, TrustedExecutionScope>()
            // Default null-returning principal provider. Hosts with an HTTP
            // pipeline replace this via AddTraxApi with an HttpContext-backed
            // implementation so per-principal concurrency caps activate.
            .AddSingleton<ICurrentPrincipalProvider, NullPrincipalProvider>()
            .AddScoped<ITrainBus, TrainBus>()
            .AddScoped<IRunExecutor, LocalRunExecutor>()
            // Singletons, because a singleton train may inject either one and ValidateScopes is
            // on by default in Development, so scoped would fail such a host at startup. Neither
            // holds per-scope state: EnqueueContextAccessor keeps its value in a static
            // AsyncLocal (it had to, or a singleton train's accessor read null), and
            // WorkQueuePromotion creates a context per call from a singleton factory.
            .AddSingleton<IEnqueueContextAccessor, EnqueueContextAccessor>()
            .AddSingleton<IWorkQueuePromotion, WorkQueuePromotion>()
            .AddScoped<ITrainExecutionService, TrainExecutionService>()
            .RegisterServiceTrains(trainRegistry.DiscoveredTrains, serviceTrainLifetime);
    }
}
