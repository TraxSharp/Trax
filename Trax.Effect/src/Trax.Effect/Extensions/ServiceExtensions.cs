using System.Reflection;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.Trax.CoreEffectBuilder;
using Trax.Effect.Configuration.Trax.CoreEffectConfiguration;
using Trax.Effect.Services.EffectProviderFactory;
using Trax.Effect.Services.EffectRegistry;
using Trax.Effect.Services.EffectRunner;
using Trax.Effect.Services.StepEffectProviderFactory;
using Trax.Effect.Services.StepEffectRunner;
using Microsoft.Extensions.DependencyInjection;

namespace Trax.Effect.Extensions;

public static class ServiceExtensions
{
    #region Configuration

    public static IServiceCollection AddTrax.CoreEffects(
        this IServiceCollection serviceCollection,
        Action<Trax.CoreEffectConfigurationBuilder>? options = null
    )
    {
        // Create the registry eagerly so AddEffect calls during configuration can register types
        var registry = new EffectRegistry();

        var configuration = BuildConfiguration(serviceCollection, options, registry);

        return serviceCollection
            .AddSingleton<IEffectRegistry>(registry)
            .AddSingleton<ITrax.CoreEffectConfiguration>(configuration)
            .AddTransient<IEffectRunner, EffectRunner>()
            .AddTransient<IStepEffectRunner, StepEffectRunner>();
    }

    private static Trax.CoreEffectConfiguration BuildConfiguration(
        IServiceCollection serviceCollection,
        Action<Trax.CoreEffectConfigurationBuilder>? options,
        IEffectRegistry registry
    )
    {
        // Create Builder to be used after Options are invoked
        var builder = new Trax.CoreEffectConfigurationBuilder(serviceCollection, registry);

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

    public static Trax.CoreEffectConfigurationBuilder AddEffect<
        TIEffectProviderFactory,
        TEffectProviderFactory
    >(
        this Trax.CoreEffectConfigurationBuilder builder,
        TEffectProviderFactory factory,
        bool toggleable = true
    )
        where TIEffectProviderFactory : class, IEffectProviderFactory
        where TEffectProviderFactory : class, TIEffectProviderFactory
    {
        builder
            .ServiceCollection.AddSingleton<TEffectProviderFactory>(factory)
            .AddSingleton<IEffectProviderFactory>(
                sp => sp.GetRequiredService<TEffectProviderFactory>()
            )
            .AddSingleton<TIEffectProviderFactory>(
                sp => sp.GetRequiredService<TEffectProviderFactory>()
            );

        builder.EffectRegistry?.Register(typeof(TEffectProviderFactory), toggleable: toggleable);

        return builder;
    }

    public static Trax.CoreEffectConfigurationBuilder AddEffect<TEffectProviderFactory>(
        this Trax.CoreEffectConfigurationBuilder builder,
        bool toggleable = true
    )
        where TEffectProviderFactory : class, IEffectProviderFactory
    {
        builder
            .ServiceCollection.AddSingleton<TEffectProviderFactory>()
            .AddSingleton<IEffectProviderFactory>(
                sp => sp.GetRequiredService<TEffectProviderFactory>()
            );

        builder.EffectRegistry?.Register(typeof(TEffectProviderFactory), toggleable: toggleable);

        return builder;
    }

    public static Trax.CoreEffectConfigurationBuilder AddEffect<
        TIEffectProviderFactory,
        TEffectProviderFactory
    >(this Trax.CoreEffectConfigurationBuilder builder, bool toggleable = true)
        where TIEffectProviderFactory : class, IEffectProviderFactory
        where TEffectProviderFactory : class, TIEffectProviderFactory
    {
        builder
            .ServiceCollection.AddSingleton<TEffectProviderFactory>()
            .AddSingleton<IEffectProviderFactory>(
                sp => sp.GetRequiredService<TEffectProviderFactory>()
            )
            .AddSingleton<TIEffectProviderFactory>(
                sp => sp.GetRequiredService<TEffectProviderFactory>()
            );

        builder.EffectRegistry?.Register(typeof(TEffectProviderFactory), toggleable: toggleable);

        return builder;
    }

    public static Trax.CoreEffectConfigurationBuilder AddEffect<TEffectProviderFactory>(
        this Trax.CoreEffectConfigurationBuilder builder,
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

    public static Trax.CoreEffectConfigurationBuilder AddStepEffect<
        TIStepEffectProviderFactory,
        TStepEffectProviderFactory
    >(
        this Trax.CoreEffectConfigurationBuilder builder,
        TStepEffectProviderFactory factory,
        bool toggleable = true
    )
        where TIStepEffectProviderFactory : class, IStepEffectProviderFactory
        where TStepEffectProviderFactory : class, TIStepEffectProviderFactory
    {
        builder
            .ServiceCollection.AddSingleton<TStepEffectProviderFactory>(factory)
            .AddSingleton<IStepEffectProviderFactory>(
                sp => sp.GetRequiredService<TStepEffectProviderFactory>()
            )
            .AddSingleton<TIStepEffectProviderFactory>(
                sp => sp.GetRequiredService<TStepEffectProviderFactory>()
            );

        builder.EffectRegistry?.Register(
            typeof(TStepEffectProviderFactory),
            toggleable: toggleable
        );

        return builder;
    }

    public static Trax.CoreEffectConfigurationBuilder AddStepEffect<TStepEffectProviderFactory>(
        this Trax.CoreEffectConfigurationBuilder builder,
        bool toggleable = true
    )
        where TStepEffectProviderFactory : class, IStepEffectProviderFactory
    {
        builder
            .ServiceCollection.AddSingleton<TStepEffectProviderFactory>()
            .AddSingleton<IStepEffectProviderFactory>(
                sp => sp.GetRequiredService<TStepEffectProviderFactory>()
            );

        builder.EffectRegistry?.Register(
            typeof(TStepEffectProviderFactory),
            toggleable: toggleable
        );

        return builder;
    }

    public static Trax.CoreEffectConfigurationBuilder AddStepEffect<TStepEffectProviderFactory>(
        this Trax.CoreEffectConfigurationBuilder builder,
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

    #region StepInjection

    public static IServiceCollection AddScopedTrax.CoreStep<TService, TImplementation>(
        this IServiceCollection services
    )
        where TService : class
        where TImplementation : class, TService
        // Nothing inherently different about the injection. Overload for posterity.
        =>
        services.AddScopedTrax.CoreRoute<TService, TImplementation>();

    public static IServiceCollection AddScopedTrax.CoreStep(
        this IServiceCollection services,
        Type serviceInterface,
        Type serviceImplementation
    )
        // Nothing inherently different about the injection. Overload for posterity.
        =>
        services.AddScopedTrax.CoreRoute(serviceInterface, serviceImplementation);

    public static IServiceCollection AddTransientTrax.CoreStep<TService, TImplementation>(
        this IServiceCollection services
    )
        where TService : class
        where TImplementation : class, TService
        // Nothing inherently different about the injection. Overload for posterity.
        =>
        services.AddTransientTrax.CoreRoute<TService, TImplementation>();

    public static IServiceCollection AddTransientTrax.CoreStep(
        this IServiceCollection services,
        Type serviceInterface,
        Type serviceImplementation
    )
        // Nothing inherently different about the injection. Overload for posterity.
        =>
        services.AddTransientTrax.CoreRoute(serviceInterface, serviceImplementation);

    public static IServiceCollection AddSingletonTrax.CoreStep<TService, TImplementation>(
        this IServiceCollection services
    )
        where TService : class
        where TImplementation : class, TService
        // Nothing inherently different about the injection. Overload for posterity.
        =>
        services.AddSingletonTrax.CoreRoute<TService, TImplementation>();

    public static IServiceCollection AddSingletonTrax.CoreStep(
        this IServiceCollection services,
        Type serviceInterface,
        Type serviceImplementation
    )
        // Nothing inherently different about the injection. Overload for posterity.
        =>
        services.AddSingletonTrax.CoreRoute(serviceInterface, serviceImplementation);

    #endregion

    #region RouteInjection

    public static IServiceCollection AddScopedTrax.CoreRoute<TService, TImplementation>(
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

    public static IServiceCollection AddScopedTrax.CoreRoute(
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

    public static IServiceCollection AddTransientTrax.CoreRoute<TService, TImplementation>(
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

    public static IServiceCollection AddTransientTrax.CoreRoute(
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

    public static IServiceCollection AddSingletonTrax.CoreRoute<TService, TImplementation>(
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

    public static IServiceCollection AddSingletonTrax.CoreRoute(
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
