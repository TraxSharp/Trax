using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    /// <remarks>
    /// Each train is registered under its own interface, the one deriving from
    /// <c>IServiceTrain&lt;TIn, TOut&gt;</c>, or under the closed <c>IServiceTrain&lt;TIn, TOut&gt;</c>
    /// when it has none. Other interfaces it or a base class implements are not considered.
    /// </remarks>
    /// <exception cref="TrainException">
    /// A train implements two train interfaces neither of which extends the other.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// <paramref name="serviceLifetime"/> is Singleton: a train instance is one run.
    /// </exception>
    public static IServiceCollection RegisterServiceTrains(
        this IServiceCollection services,
        ServiceLifetime serviceLifetime = ServiceLifetime.Transient,
        params Assembly[] assemblies
    )
    {
        RefuseSingletonTrainLifetime(
            serviceLifetime,
            nameof(serviceLifetime),
            nameof(RegisterServiceTrains)
        );

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
                .Select(type => (TrainServiceType.Select(type), type));

            types.AddRange(trainTypes);
        }

        foreach (var (typeInterface, typeImplementation) in types)
        {
            switch (serviceLifetime)
            {
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
    /// before the chain check had refused the host: the host was then disposed under those runs,
    /// leaving their rows <c>InProgress</c> and holding their subjects.
    /// <para>
    /// Order alone does not settle it. Under <c>HostOptions.ServicesStartConcurrently</c> every
    /// <c>StartAsync</c> begins at once, so the gates are <see cref="IHostedLifecycleService"/>s
    /// and check in <c>StartingAsync</c>, which the host finishes for every service before it calls
    /// any <c>StartAsync</c>, concurrent or not. Prepending still puts them first among the
    /// <c>StartingAsync</c> calls of the services the host registered earlier, so a host that starts
    /// its services one after another stops at the gate before starting anyone else's lifecycle
    /// hook. A startup gate that only sometimes runs first is not a gate.
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
        RefuseSingletonTrainLifetime(
            serviceLifetime,
            nameof(serviceLifetime),
            nameof(RegisterServiceTrains)
        );

        foreach (var (serviceType, implementationType) in trains)
        {
            switch (serviceLifetime)
            {
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
    /// Refuses a singleton lifetime for discovered trains, naming the setting that asked for it.
    /// </summary>
    /// <remarks>
    /// Trax.Effect refuses a singleton service train too, but its message names the train, not the
    /// mediator setting that chose the lifetime for every discovered train. A train instance carries
    /// the state of the run in progress, so one instance shared by the process would mix concurrent
    /// runs together.
    /// </remarks>
    internal static void RefuseSingletonTrainLifetime(
        ServiceLifetime lifetime,
        string parameterName,
        string setting
    )
    {
        if (lifetime == ServiceLifetime.Singleton)
            throw new ArgumentException(
                $"{setting} cannot register trains as singletons. A train instance carries the state of the run in progress, so one instance shared by the process would mix concurrent runs together. Use ServiceLifetime.Transient (the default) or ServiceLifetime.Scoped.",
                parameterName
            );
    }

    /// <summary>
    /// Registers what <see cref="AddMediator(TraxBuilderWithEffects, Func{TraxMediatorBuilder, TraxMediatorBuilder})"/>
    /// registers: the train bus, registry, discovery, execution service, concurrency limiter,
    /// trusted scope, the startup checks, and every train found in <paramref name="assemblies"/>.
    /// </summary>
    /// <remarks>
    /// Prefer <c>AddMediator</c>, which also takes the mediator's settings. Called on its own, this
    /// registers a <see cref="MediatorConfiguration"/> with the defaults (authorization required,
    /// chains verified, no concurrency limits) when none is registered yet, so the host it builds
    /// can start. A <see cref="MediatorConfiguration"/> already registered, as <c>AddMediator</c>
    /// registers one, is kept. Effects must still be configured with <c>AddTrax</c>.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// <paramref name="serviceTrainLifetime"/> is Singleton: a train instance is one run.
    /// </exception>
    public static IServiceCollection AddServiceTrainBus(
        this IServiceCollection serviceCollection,
        ServiceLifetime serviceTrainLifetime = ServiceLifetime.Transient,
        params Assembly[] assemblies
    )
    {
        RefuseSingletonTrainLifetime(
            serviceTrainLifetime,
            nameof(serviceTrainLifetime),
            nameof(AddServiceTrainBus)
        );

        var trainRegistry = new TrainRegistry(assemblies);

        // The concurrency limiter, the execution service and both startup checks need one, so a
        // collection without it cannot start. AddMediator registers its own first, which wins.
        serviceCollection.TryAddSingleton(
            new MediatorConfiguration
            {
                TrainLifetime = serviceTrainLifetime,
                Assemblies = [.. assemblies],
            }
        );

        // Prepended, not appended, so they run before any hosted service the host registered before
        // it called into Trax. Reverse order, because each goes to the front: the chain check ends
        // up first.
        serviceCollection.PrependHostedService<AuthorizationRegistrationValidator>();
        serviceCollection.PrependHostedService<Services.ChainVerification.TrainChainStartupValidator>();

        return serviceCollection
            .AddSingleton<IServiceCollection>(serviceCollection)
            .AddSingleton<ITrainRegistry>(trainRegistry)
            .AddSingleton<ITrainDiscoveryService, TrainDiscoveryService>()
            .AddSingleton<
                Services.ChainVerification.ITrainChainGraphs,
                Services.ChainVerification.TrainChainGraphs
            >()
            .AddSingleton<IConcurrencyLimiter, ConcurrencyLimiter>()
            .AddSingleton<ITrustedExecutionScope, TrustedExecutionScope>()
            // Default null-returning principal provider. Hosts with an HTTP
            // pipeline replace this via AddTraxApi with an HttpContext-backed
            // implementation so per-principal concurrency caps activate.
            .AddSingleton<ICurrentPrincipalProvider, NullPrincipalProvider>()
            .AddScoped<ITrainBus, TrainBus>()
            .AddScoped<IRunExecutor, LocalRunExecutor>()
            // Singletons, because a singleton the host registers may inject either one and
            // ValidateScopes is on by default in Development, so scoped would fail such a host at
            // startup. (Trains themselves are never singletons: the mediator refuses that
            // lifetime.) Neither holds per-scope state: EnqueueContextAccessor keeps its value in
            // a static AsyncLocal, so a singleton's accessor reads the current enqueue, and
            // WorkQueuePromotion creates a context per call from a singleton factory.
            .AddSingleton<IEnqueueContextAccessor, EnqueueContextAccessor>()
            .AddSingleton<IWorkQueuePromotion, WorkQueuePromotion>()
            .AddScoped<ITrainExecutionService, TrainExecutionService>()
            // Queues the train a state machine's invoking state runs, through the execution service
            // above and into the caller's transaction. Registered whether or not the host has
            // machines; AddStateMachines' startup check refuses a machine that invokes trains
            // without it.
            .AddScoped<
                Trax.Effect.StateMachine.Persistence.IInvokedTrainLauncher,
                Services.InvokedTrains.InvokedTrainLauncher
            >()
            .RegisterServiceTrains(trainRegistry.DiscoveredTrains, serviceTrainLifetime);
    }
}
