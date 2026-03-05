using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Attributes;
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

    public static IServiceCollection AddTraxEffects(
        this IServiceCollection serviceCollection,
        Action<TraxEffectConfigurationBuilder>? options = null
    )
    {
        // Create the registry eagerly so AddEffect calls during configuration can register types
        var registry = new EffectRegistry();

        var configuration = BuildConfiguration(serviceCollection, options, registry);

        return serviceCollection
            .AddSingleton<IEffectRegistry>(registry)
            .AddSingleton<ITraxEffectConfiguration>(configuration)
            .AddTransient<IEffectRunner, EffectRunner>()
            .AddTransient<IStepEffectRunner, StepEffectRunner>()
            .AddTransient<ILifecycleHookRunner, LifecycleHookRunner>();
    }

    private static TraxEffectConfiguration BuildConfiguration(
        IServiceCollection serviceCollection,
        Action<TraxEffectConfigurationBuilder>? options,
        IEffectRegistry registry
    )
    {
        // Create Builder to be used after Options are invoked
        var builder = new TraxEffectConfigurationBuilder(serviceCollection, registry);

        // Options able to be null since all values have defaults
        options?.Invoke(builder);

        return builder.Build();
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

    public static TraxEffectConfigurationBuilder AddEffect<
        TIEffectProviderFactory,
        TEffectProviderFactory
    >(
        this TraxEffectConfigurationBuilder builder,
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

    public static TraxEffectConfigurationBuilder AddEffect<TEffectProviderFactory>(
        this TraxEffectConfigurationBuilder builder,
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

    public static TraxEffectConfigurationBuilder AddEffect<
        TIEffectProviderFactory,
        TEffectProviderFactory
    >(this TraxEffectConfigurationBuilder builder, bool toggleable = true)
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

    public static TraxEffectConfigurationBuilder AddEffect<TEffectProviderFactory>(
        this TraxEffectConfigurationBuilder builder,
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

    public static TraxEffectConfigurationBuilder AddStepEffect<
        TIStepEffectProviderFactory,
        TStepEffectProviderFactory
    >(
        this TraxEffectConfigurationBuilder builder,
        TStepEffectProviderFactory factory,
        bool toggleable = true
    )
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

    public static TraxEffectConfigurationBuilder AddStepEffect<TStepEffectProviderFactory>(
        this TraxEffectConfigurationBuilder builder,
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

    public static TraxEffectConfigurationBuilder AddStepEffect<TStepEffectProviderFactory>(
        this TraxEffectConfigurationBuilder builder,
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

    public static TraxEffectConfigurationBuilder AddLifecycleHook<
        TILifecycleHookFactory,
        TLifecycleHookFactory
    >(
        this TraxEffectConfigurationBuilder builder,
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

    public static TraxEffectConfigurationBuilder AddLifecycleHook<TLifecycleHookFactory>(
        this TraxEffectConfigurationBuilder builder,
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

    public static TraxEffectConfigurationBuilder AddLifecycleHook<TLifecycleHookFactory>(
        this TraxEffectConfigurationBuilder builder,
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
        where TImplementation : class, TService
        // Nothing inherently different about the injection. Overload for posterity.
        =>
        services.AddScopedTraxRoute<TService, TImplementation>();

    public static IServiceCollection AddScopedTraxStep(
        this IServiceCollection services,
        Type serviceInterface,
        Type serviceImplementation
    )
        // Nothing inherently different about the injection. Overload for posterity.
        =>
        services.AddScopedTraxRoute(serviceInterface, serviceImplementation);

    public static IServiceCollection AddTransientTraxStep<TService, TImplementation>(
        this IServiceCollection services
    )
        where TService : class
        where TImplementation : class, TService
        // Nothing inherently different about the injection. Overload for posterity.
        =>
        services.AddTransientTraxRoute<TService, TImplementation>();

    public static IServiceCollection AddTransientTraxStep(
        this IServiceCollection services,
        Type serviceInterface,
        Type serviceImplementation
    )
        // Nothing inherently different about the injection. Overload for posterity.
        =>
        services.AddTransientTraxRoute(serviceInterface, serviceImplementation);

    public static IServiceCollection AddSingletonTraxStep<TService, TImplementation>(
        this IServiceCollection services
    )
        where TService : class
        where TImplementation : class, TService
        // Nothing inherently different about the injection. Overload for posterity.
        =>
        services.AddSingletonTraxRoute<TService, TImplementation>();

    public static IServiceCollection AddSingletonTraxStep(
        this IServiceCollection services,
        Type serviceInterface,
        Type serviceImplementation
    )
        // Nothing inherently different about the injection. Overload for posterity.
        =>
        services.AddSingletonTraxRoute(serviceInterface, serviceImplementation);

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
