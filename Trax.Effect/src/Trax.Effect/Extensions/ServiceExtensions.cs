using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Configuration.TraxEffectBuilder;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Services.EffectProviderFactory;
using Trax.Effect.Services.EffectRegistry;
using Trax.Effect.Services.EffectRunner;
using Trax.Effect.Services.LifecycleHookRunner;
using Trax.Effect.Services.StepEffectProviderFactory;
using Trax.Effect.Services.StepEffectRunner;
using Trax.Effect.Services.TrainLifecycleHookFactory;

namespace Trax.Effect.Extensions;

public static class ServiceExtensions
{
    #region Configuration

    /// <summary>
    /// Registers the Trax system with the dependency injection container.
    /// </summary>
    /// <remarks>
    /// This is the root entry point for all Trax configuration. Each subsystem
    /// (effects, mediator, scheduler) has its own scoped builder:
    /// <code>
    /// services.AddTrax(trax => trax
    ///     .AddEffects(effects => effects
    ///         .UsePostgres(connectionString)
    ///         .AddJson()
    ///         .SaveTrainParameters()
    ///     )
    ///     .AddMediator(typeof(Program).Assembly)
    ///     .AddScheduler(scheduler => scheduler
    ///         .UseLocalWorkers()
    ///         .Schedule&lt;IMyTrain&gt;(...)
    ///     )
    /// );
    /// </code>
    /// </remarks>
    public static IServiceCollection AddTrax(
        this IServiceCollection services,
        Action<TraxBuilder> configure
    )
    {
        var registry = new EffectRegistry();
        var builder = new TraxBuilder(services, registry);

        configure(builder);

        // Use effect configuration from AddEffects(), or defaults if not called
        var effectConfig =
            builder.EffectConfiguration
            ?? new Trax.Effect.Configuration.TraxEffectConfiguration.TraxEffectConfiguration();

        // Marker so AddTraxDashboard() / AddTraxGraphQL() can verify AddTrax() was called
        services.AddSingleton<TraxMarker>();

        return services
            .AddSingleton<IEffectRegistry>(registry)
            .AddSingleton<ITraxEffectConfiguration>(effectConfig)
            .AddTransient<IEffectRunner, EffectRunner>()
            .AddTransient<IStepEffectRunner, StepEffectRunner>()
            .AddTransient<ILifecycleHookRunner, LifecycleHookRunner>();
    }

    /// <summary>
    /// Configures the Trax effect system (data providers, step providers, lifecycle hooks).
    /// </summary>
    /// <returns>A <see cref="TraxBuilderWithEffects"/> that enables chaining <c>AddMediator()</c>.</returns>
    public static TraxBuilderWithEffects AddEffects(
        this TraxBuilder builder,
        Action<TraxEffectBuilder> configure
    )
    {
        var effectBuilder = new TraxEffectBuilder(builder);
        configure(effectBuilder);
        builder.EffectConfiguration = effectBuilder.Build();
        return new TraxBuilderWithEffects(builder);
    }

    public static void InjectProperties(this IServiceProvider serviceProvider, object instance)
    {
        var properties = instance
            .GetType()
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.IsDefined(typeof(InjectAttribute)) && p.CanWrite);

        foreach (var property in properties)
        {
            if (property.GetValue(instance) != null)
                continue;

            var propertyType = property.PropertyType;
            object? service = null;

            // Handle IEnumerable<T>
            if (
                propertyType.IsGenericType
                && propertyType.GetGenericTypeDefinition() == typeof(IEnumerable<>)
            )
            {
                var serviceType = propertyType.GetGenericArguments()[0];
                var serviceCollectionType = typeof(IEnumerable<>).MakeGenericType(serviceType);
                service = serviceProvider.GetService(serviceCollectionType);
            }
            else
            {
                service = serviceProvider.GetService(propertyType);
            }

            if (service != null)
            {
                property.SetValue(instance, service);
            }
        }
    }

    #endregion

    #region Effect

    public static TraxEffectBuilder AddEffect<TIEffectProviderFactory, TEffectProviderFactory>(
        this TraxEffectBuilder builder,
        TEffectProviderFactory factory,
        bool toggleable = true
    )
        where TIEffectProviderFactory : class, IEffectProviderFactory
        where TEffectProviderFactory : class, TIEffectProviderFactory
    {
        builder
            .ServiceCollection.AddSingleton<TEffectProviderFactory>(factory)
            .AddSingleton<IEffectProviderFactory>(sp =>
                sp.GetRequiredService<TEffectProviderFactory>()
            )
            .AddSingleton<TIEffectProviderFactory>(sp =>
                sp.GetRequiredService<TEffectProviderFactory>()
            );

        builder.EffectRegistry?.Register(typeof(TEffectProviderFactory), toggleable: toggleable);

        return builder;
    }

    public static TraxEffectBuilder AddEffect<TEffectProviderFactory>(
        this TraxEffectBuilder builder,
        bool toggleable = true
    )
        where TEffectProviderFactory : class, IEffectProviderFactory
    {
        builder
            .ServiceCollection.AddSingleton<TEffectProviderFactory>()
            .AddSingleton<IEffectProviderFactory>(sp =>
                sp.GetRequiredService<TEffectProviderFactory>()
            );

        builder.EffectRegistry?.Register(typeof(TEffectProviderFactory), toggleable: toggleable);

        return builder;
    }

    public static TraxEffectBuilder AddEffect<TIEffectProviderFactory, TEffectProviderFactory>(
        this TraxEffectBuilder builder,
        bool toggleable = true
    )
        where TIEffectProviderFactory : class, IEffectProviderFactory
        where TEffectProviderFactory : class, TIEffectProviderFactory
    {
        builder
            .ServiceCollection.AddSingleton<TEffectProviderFactory>()
            .AddSingleton<IEffectProviderFactory>(sp =>
                sp.GetRequiredService<TEffectProviderFactory>()
            )
            .AddSingleton<TIEffectProviderFactory>(sp =>
                sp.GetRequiredService<TEffectProviderFactory>()
            );

        builder.EffectRegistry?.Register(typeof(TEffectProviderFactory), toggleable: toggleable);

        return builder;
    }

    public static TraxEffectBuilder AddEffect<TEffectProviderFactory>(
        this TraxEffectBuilder builder,
        TEffectProviderFactory factory,
        bool toggleable = true
    )
        where TEffectProviderFactory : class, IEffectProviderFactory
    {
        builder.ServiceCollection.AddSingleton<IEffectProviderFactory>(factory);

        builder.EffectRegistry?.Register(typeof(TEffectProviderFactory), toggleable: toggleable);

        return builder;
    }

    #endregion

    #region StepEffect

    public static TraxEffectBuilder AddStepEffect<
        TIStepEffectProviderFactory,
        TStepEffectProviderFactory
    >(this TraxEffectBuilder builder, TStepEffectProviderFactory factory, bool toggleable = true)
        where TIStepEffectProviderFactory : class, IStepEffectProviderFactory
        where TStepEffectProviderFactory : class, TIStepEffectProviderFactory
    {
        builder
            .ServiceCollection.AddSingleton<TStepEffectProviderFactory>(factory)
            .AddSingleton<IStepEffectProviderFactory>(sp =>
                sp.GetRequiredService<TStepEffectProviderFactory>()
            )
            .AddSingleton<TIStepEffectProviderFactory>(sp =>
                sp.GetRequiredService<TStepEffectProviderFactory>()
            );

        builder.EffectRegistry?.Register(
            typeof(TStepEffectProviderFactory),
            toggleable: toggleable
        );

        return builder;
    }

    public static TraxEffectBuilder AddStepEffect<TStepEffectProviderFactory>(
        this TraxEffectBuilder builder,
        bool toggleable = true
    )
        where TStepEffectProviderFactory : class, IStepEffectProviderFactory
    {
        builder
            .ServiceCollection.AddSingleton<TStepEffectProviderFactory>()
            .AddSingleton<IStepEffectProviderFactory>(sp =>
                sp.GetRequiredService<TStepEffectProviderFactory>()
            );

        builder.EffectRegistry?.Register(
            typeof(TStepEffectProviderFactory),
            toggleable: toggleable
        );

        return builder;
    }

    public static TraxEffectBuilder AddStepEffect<TStepEffectProviderFactory>(
        this TraxEffectBuilder builder,
        TStepEffectProviderFactory factory,
        bool toggleable = true
    )
        where TStepEffectProviderFactory : class, IStepEffectProviderFactory
    {
        builder.ServiceCollection.AddSingleton<IStepEffectProviderFactory>(factory);

        builder.EffectRegistry?.Register(
            typeof(TStepEffectProviderFactory),
            toggleable: toggleable
        );

        return builder;
    }

    #endregion

    #region LifecycleHook

    public static TraxEffectBuilder AddLifecycleHook<TILifecycleHookFactory, TLifecycleHookFactory>(
        this TraxEffectBuilder builder,
        TLifecycleHookFactory factory,
        bool toggleable = true
    )
        where TILifecycleHookFactory : class, ITrainLifecycleHookFactory
        where TLifecycleHookFactory : class, TILifecycleHookFactory
    {
        builder
            .ServiceCollection.AddSingleton<TLifecycleHookFactory>(factory)
            .AddSingleton<ITrainLifecycleHookFactory>(sp =>
                sp.GetRequiredService<TLifecycleHookFactory>()
            )
            .AddSingleton<TILifecycleHookFactory>(sp =>
                sp.GetRequiredService<TLifecycleHookFactory>()
            );

        builder.EffectRegistry?.Register(typeof(TLifecycleHookFactory), toggleable: toggleable);

        return builder;
    }

    public static TraxEffectBuilder AddLifecycleHook<TLifecycleHookFactory>(
        this TraxEffectBuilder builder,
        bool toggleable = true
    )
        where TLifecycleHookFactory : class, ITrainLifecycleHookFactory
    {
        builder
            .ServiceCollection.AddSingleton<TLifecycleHookFactory>()
            .AddSingleton<ITrainLifecycleHookFactory>(sp =>
                sp.GetRequiredService<TLifecycleHookFactory>()
            );

        builder.EffectRegistry?.Register(typeof(TLifecycleHookFactory), toggleable: toggleable);

        return builder;
    }

    public static TraxEffectBuilder AddLifecycleHook<TLifecycleHookFactory>(
        this TraxEffectBuilder builder,
        TLifecycleHookFactory factory,
        bool toggleable = true
    )
        where TLifecycleHookFactory : class, ITrainLifecycleHookFactory
    {
        builder.ServiceCollection.AddSingleton<ITrainLifecycleHookFactory>(factory);

        builder.EffectRegistry?.Register(typeof(TLifecycleHookFactory), toggleable: toggleable);

        return builder;
    }

    #endregion

    #region StepInjection

    public static IServiceCollection AddScopedTraxStep<TService, TImplementation>(
        this IServiceCollection services
    )
        where TService : class
        where TImplementation : class, TService =>
        services.AddScopedTraxRoute<TService, TImplementation>();

    public static IServiceCollection AddScopedTraxStep(
        this IServiceCollection services,
        Type serviceInterface,
        Type serviceImplementation
    ) => services.AddScopedTraxRoute(serviceInterface, serviceImplementation);

    public static IServiceCollection AddTransientTraxStep<TService, TImplementation>(
        this IServiceCollection services
    )
        where TService : class
        where TImplementation : class, TService =>
        services.AddTransientTraxRoute<TService, TImplementation>();

    public static IServiceCollection AddTransientTraxStep(
        this IServiceCollection services,
        Type serviceInterface,
        Type serviceImplementation
    ) => services.AddTransientTraxRoute(serviceInterface, serviceImplementation);

    public static IServiceCollection AddSingletonTraxStep<TService, TImplementation>(
        this IServiceCollection services
    )
        where TService : class
        where TImplementation : class, TService =>
        services.AddSingletonTraxRoute<TService, TImplementation>();

    public static IServiceCollection AddSingletonTraxStep(
        this IServiceCollection services,
        Type serviceInterface,
        Type serviceImplementation
    ) => services.AddSingletonTraxRoute(serviceInterface, serviceImplementation);

    #endregion

    #region RouteInjection

    public static IServiceCollection AddScopedTraxRoute<TService, TImplementation>(
        this IServiceCollection services
    )
        where TService : class
        where TImplementation : class, TService
    {
        services.AddScoped<TImplementation>();
        services.AddScoped<TService>(sp =>
        {
            var instance = sp.GetRequiredService<TImplementation>();
            sp.InjectProperties(instance);
            return instance;
        });

        return services;
    }

    public static IServiceCollection AddScopedTraxRoute(
        this IServiceCollection services,
        Type serviceInterface,
        Type serviceImplementation
    )
    {
        services.AddScoped(serviceImplementation);
        services.AddScoped(
            serviceInterface,
            sp =>
            {
                var instance = sp.GetRequiredService(serviceImplementation);
                sp.InjectProperties(instance);
                return instance;
            }
        );

        return services;
    }

    public static IServiceCollection AddTransientTraxRoute<TService, TImplementation>(
        this IServiceCollection services
    )
        where TService : class
        where TImplementation : class, TService
    {
        services.AddTransient<TImplementation>();
        services.AddTransient<TService>(sp =>
        {
            var instance = sp.GetRequiredService<TImplementation>();
            sp.InjectProperties(instance);
            return instance;
        });

        return services;
    }

    public static IServiceCollection AddTransientTraxRoute(
        this IServiceCollection services,
        Type serviceInterface,
        Type serviceImplementation
    )
    {
        services.AddTransient(serviceImplementation);
        services.AddTransient(
            serviceInterface,
            sp =>
            {
                var instance = sp.GetRequiredService(serviceImplementation);
                sp.InjectProperties(instance);
                return instance;
            }
        );

        return services;
    }

    public static IServiceCollection AddSingletonTraxRoute<TService, TImplementation>(
        this IServiceCollection services
    )
        where TService : class
        where TImplementation : class, TService
    {
        services.AddSingleton<TImplementation>();
        services.AddSingleton<TService>(sp =>
        {
            var instance = sp.GetRequiredService<TImplementation>();
            sp.InjectProperties(instance);
            return instance;
        });

        return services;
    }

    public static IServiceCollection AddSingletonTraxRoute(
        this IServiceCollection services,
        Type serviceInterface,
        Type serviceImplementation
    )
    {
        services.AddSingleton(serviceImplementation);
        services.AddSingleton(
            serviceInterface,
            sp =>
            {
                var instance = sp.GetRequiredService(serviceImplementation);
                sp.InjectProperties(instance);
                return instance;
            }
        );

        return services;
    }

    #endregion
}
