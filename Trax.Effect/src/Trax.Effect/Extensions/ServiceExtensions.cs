using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Configuration.TraxEffectBuilder;
using Trax.Effect.Configuration.TraxEffectConfiguration;
using Trax.Effect.Services.EffectProviderFactory;
using Trax.Effect.Services.EffectRegistry;
using Trax.Effect.Services.EffectRunner;
using Trax.Effect.Services.JunctionEffectProviderFactory;
using Trax.Effect.Services.JunctionEffectRunner;
using Trax.Effect.Services.LifecycleHookRunner;
using Trax.Effect.Services.TrainLifecycleHook;
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

        // Build and set the process-wide host identity
        var hostInfo = Models.Host.TraxHostInfo.AutoDetect();
        if (builder.HostEnvironmentOverride != null)
            hostInfo = hostInfo with { HostEnvironment = builder.HostEnvironmentOverride };
        if (builder.HostInstanceIdOverride != null)
            hostInfo = hostInfo with { HostInstanceId = builder.HostInstanceIdOverride };
        if (builder.HostLabels.Count > 0)
        {
            var mergedLabels = new Dictionary<string, string>(hostInfo.Labels);
            foreach (var (key, value) in builder.HostLabels)
                mergedLabels[key] = value;
            hostInfo = hostInfo with { Labels = mergedLabels };
        }
        Models.Host.TraxHostInfo.Current = hostInfo;

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
            .AddTransient<IJunctionEffectRunner, JunctionEffectRunner>()
            .AddTransient<ILifecycleHookRunner, LifecycleHookRunner>();
    }

    /// <summary>
    /// Configures the Trax effect system (data providers, junction providers, lifecycle hooks).
    /// </summary>
    /// <remarks>
    /// The configure function receives a <see cref="TraxEffectBuilder"/> and returns the
    /// (potentially promoted) builder. When a data provider is configured via <c>UsePostgres()</c>
    /// or <c>UseInMemory()</c>, the builder is promoted to
    /// <see cref="Configuration.TraxEffectBuilder.TraxEffectBuilderWithData"/>,
    /// unlocking additional methods like <c>AddDataContextLogging()</c>.
    ///
    /// If no data provider is configured, features that require one (such as <c>AddScheduler()</c>
    /// or <c>AddJunctionProgress()</c>) will throw at build time with a helpful error message.
    ///
    /// <code>
    /// services.AddTrax(trax => trax
    ///     .AddEffects(effects => effects
    ///         .UsePostgres(connectionString)
    ///         .AddDataContextLogging()
    ///         .AddJson()
    ///     )
    /// );
    /// </code>
    /// </remarks>
    /// <param name="builder">The root Trax builder.</param>
    /// <param name="configure">
    /// A function that configures the effect builder. Return the builder from the last chained call.
    /// </param>
    /// <returns>A <see cref="TraxBuilderWithEffects"/> that enables chaining <c>AddMediator()</c>.</returns>
    public static TraxBuilderWithEffects AddEffects(
        this TraxBuilder builder,
        Func<TraxEffectBuilder, TraxEffectBuilder> configure
    )
    {
        var effectBuilder = new TraxEffectBuilder(builder);
        var result = configure(effectBuilder);
        builder.EffectConfiguration = result.Build();
        return new TraxBuilderWithEffects(builder);
    }

    /// <summary>
    /// Configures the Trax effect system with default settings (no data provider).
    /// </summary>
    /// <remarks>
    /// This overload does not register a data provider. If you need data persistence
    /// (required by <c>AddScheduler()</c>, <c>AddJunctionProgress()</c>, etc.), use
    /// the overload that accepts a configure function and call <c>UsePostgres()</c>
    /// or <c>UseInMemory()</c>.
    /// </remarks>
    /// <param name="builder">The root Trax builder.</param>
    /// <returns>A <see cref="TraxBuilderWithEffects"/> that enables chaining <c>AddMediator()</c>.</returns>
    public static TraxBuilderWithEffects AddEffects(this TraxBuilder builder)
    {
        return builder.AddEffects(effects => effects);
    }

    /// <summary>
    /// Injects services into properties decorated with <see cref="InjectAttribute"/> on the given instance.
    /// Properties that already have a non-null value are skipped. Supports <see cref="IEnumerable{T}"/> properties.
    /// </summary>
    /// <param name="serviceProvider">The service provider to resolve dependencies from.</param>
    /// <param name="instance">The object whose <see cref="InjectAttribute"/>-decorated properties will be populated.</param>
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

    /// <summary>
    /// Registers a train-level effect provider with both its interface and implementation type,
    /// using an existing factory instance. Effects run before/after each train execution.
    /// </summary>
    /// <typeparam name="TIEffectProviderFactory">The effect provider factory interface.</typeparam>
    /// <typeparam name="TEffectProviderFactory">The concrete effect provider factory type.</typeparam>
    /// <param name="builder">The effect builder.</param>
    /// <param name="factory">The factory instance to register.</param>
    /// <param name="toggleable">Whether this effect can be toggled on/off at runtime. Defaults to <c>true</c>.</param>
    /// <returns>The effect builder for chaining.</returns>
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

    /// <summary>
    /// Registers a train-level effect provider resolved from DI.
    /// Effects run before/after each train execution.
    /// </summary>
    /// <typeparam name="TEffectProviderFactory">The concrete effect provider factory type.</typeparam>
    /// <param name="builder">The effect builder.</param>
    /// <param name="toggleable">Whether this effect can be toggled on/off at runtime. Defaults to <c>true</c>.</param>
    /// <returns>The effect builder for chaining.</returns>
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

    /// <summary>
    /// Registers a train-level effect provider with both its interface and implementation type,
    /// resolved from DI. Effects run before/after each train execution.
    /// </summary>
    /// <typeparam name="TIEffectProviderFactory">The effect provider factory interface.</typeparam>
    /// <typeparam name="TEffectProviderFactory">The concrete effect provider factory type.</typeparam>
    /// <param name="builder">The effect builder.</param>
    /// <param name="toggleable">Whether this effect can be toggled on/off at runtime. Defaults to <c>true</c>.</param>
    /// <returns>The effect builder for chaining.</returns>
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

    /// <summary>
    /// Registers a train-level effect provider using an existing factory instance.
    /// Effects run before/after each train execution.
    /// </summary>
    /// <typeparam name="TEffectProviderFactory">The concrete effect provider factory type.</typeparam>
    /// <param name="builder">The effect builder.</param>
    /// <param name="factory">The factory instance to register.</param>
    /// <param name="toggleable">Whether this effect can be toggled on/off at runtime. Defaults to <c>true</c>.</param>
    /// <returns>The effect builder for chaining.</returns>
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

    #region JunctionEffect

    /// <summary>
    /// Registers a junction-level effect provider with both its interface and implementation type,
    /// using an existing factory instance. Junction effects run before/after each individual junction.
    /// </summary>
    /// <typeparam name="TIJunctionEffectProviderFactory">The junction effect provider factory interface.</typeparam>
    /// <typeparam name="TJunctionEffectProviderFactory">The concrete junction effect provider factory type.</typeparam>
    /// <param name="builder">The effect builder.</param>
    /// <param name="factory">The factory instance to register.</param>
    /// <param name="toggleable">Whether this effect can be toggled on/off at runtime. Defaults to <c>true</c>.</param>
    /// <returns>The effect builder for chaining.</returns>
    public static TraxEffectBuilder AddJunctionEffect<
        TIJunctionEffectProviderFactory,
        TJunctionEffectProviderFactory
    >(
        this TraxEffectBuilder builder,
        TJunctionEffectProviderFactory factory,
        bool toggleable = true
    )
        where TIJunctionEffectProviderFactory : class, IJunctionEffectProviderFactory
        where TJunctionEffectProviderFactory : class, TIJunctionEffectProviderFactory
    {
        builder
            .ServiceCollection.AddSingleton<TJunctionEffectProviderFactory>(factory)
            .AddSingleton<IJunctionEffectProviderFactory>(sp =>
                sp.GetRequiredService<TJunctionEffectProviderFactory>()
            )
            .AddSingleton<TIJunctionEffectProviderFactory>(sp =>
                sp.GetRequiredService<TJunctionEffectProviderFactory>()
            );

        builder.EffectRegistry?.Register(
            typeof(TJunctionEffectProviderFactory),
            toggleable: toggleable
        );

        return builder;
    }

    /// <summary>
    /// Registers a junction-level effect provider resolved from DI.
    /// Junction effects run before/after each individual junction.
    /// </summary>
    /// <typeparam name="TJunctionEffectProviderFactory">The concrete junction effect provider factory type.</typeparam>
    /// <param name="builder">The effect builder.</param>
    /// <param name="toggleable">Whether this effect can be toggled on/off at runtime. Defaults to <c>true</c>.</param>
    /// <returns>The effect builder for chaining.</returns>
    public static TraxEffectBuilder AddJunctionEffect<TJunctionEffectProviderFactory>(
        this TraxEffectBuilder builder,
        bool toggleable = true
    )
        where TJunctionEffectProviderFactory : class, IJunctionEffectProviderFactory
    {
        builder
            .ServiceCollection.AddSingleton<TJunctionEffectProviderFactory>()
            .AddSingleton<IJunctionEffectProviderFactory>(sp =>
                sp.GetRequiredService<TJunctionEffectProviderFactory>()
            );

        builder.EffectRegistry?.Register(
            typeof(TJunctionEffectProviderFactory),
            toggleable: toggleable
        );

        return builder;
    }

    /// <summary>
    /// Registers a junction-level effect provider using an existing factory instance.
    /// Junction effects run before/after each individual junction.
    /// </summary>
    /// <typeparam name="TJunctionEffectProviderFactory">The concrete junction effect provider factory type.</typeparam>
    /// <param name="builder">The effect builder.</param>
    /// <param name="factory">The factory instance to register.</param>
    /// <param name="toggleable">Whether this effect can be toggled on/off at runtime. Defaults to <c>true</c>.</param>
    /// <returns>The effect builder for chaining.</returns>
    public static TraxEffectBuilder AddJunctionEffect<TJunctionEffectProviderFactory>(
        this TraxEffectBuilder builder,
        TJunctionEffectProviderFactory factory,
        bool toggleable = true
    )
        where TJunctionEffectProviderFactory : class, IJunctionEffectProviderFactory
    {
        builder.ServiceCollection.AddSingleton<IJunctionEffectProviderFactory>(factory);

        builder.EffectRegistry?.Register(
            typeof(TJunctionEffectProviderFactory),
            toggleable: toggleable
        );

        return builder;
    }

    #endregion

    #region LifecycleHook

    /// <summary>
    /// Registers a train lifecycle hook or hook factory.
    /// <para>
    /// If <typeparamref name="T"/> implements <see cref="ITrainLifecycleHook"/>, a factory is
    /// created internally — no need to write a separate factory class. The hook is resolved from DI
    /// on each train execution, so constructor-injected dependencies work.
    /// </para>
    /// <para>
    /// If <typeparamref name="T"/> implements <see cref="ITrainLifecycleHookFactory"/>, the factory
    /// is registered directly (advanced usage).
    /// </para>
    /// </summary>
    /// <typeparam name="T">
    /// Either a concrete <see cref="ITrainLifecycleHook"/> type or an <see cref="ITrainLifecycleHookFactory"/> type.
    /// </typeparam>
    /// <param name="builder">The effect builder.</param>
    /// <param name="toggleable">Whether this hook can be toggled on/off at runtime. Defaults to <c>true</c>.</param>
    /// <returns>The effect builder for chaining.</returns>
    public static TraxEffectBuilder AddLifecycleHook<T>(
        this TraxEffectBuilder builder,
        bool toggleable = true
    )
        where T : class
    {
        if (typeof(ITrainLifecycleHookFactory).IsAssignableFrom(typeof(T)))
        {
            builder
                .ServiceCollection.AddSingleton(typeof(T))
                .AddSingleton<ITrainLifecycleHookFactory>(sp =>
                    (ITrainLifecycleHookFactory)sp.GetRequiredService(typeof(T))
                );

            builder.EffectRegistry?.Register(typeof(T), toggleable: toggleable);
        }
        else if (typeof(ITrainLifecycleHook).IsAssignableFrom(typeof(T)))
        {
            builder.ServiceCollection.AddTransient(typeof(T));

            var factoryType = typeof(LifecycleHookFactory<>).MakeGenericType(typeof(T));
            builder.ServiceCollection.AddSingleton(factoryType);
            builder.ServiceCollection.AddSingleton<ITrainLifecycleHookFactory>(sp =>
                (ITrainLifecycleHookFactory)sp.GetRequiredService(factoryType)
            );

            builder.EffectRegistry?.Register(factoryType, toggleable: toggleable);
        }
        else
        {
            throw new InvalidOperationException(
                $"AddLifecycleHook<{typeof(T).Name}>() requires a type that implements "
                    + $"ITrainLifecycleHook or ITrainLifecycleHookFactory."
            );
        }

        return builder;
    }

    /// <summary>
    /// Registers a train lifecycle hook with both its interface and implementation type,
    /// using an existing factory instance. Lifecycle hooks run at train start/completion/failure boundaries.
    /// </summary>
    /// <typeparam name="TILifecycleHookFactory">The lifecycle hook factory interface.</typeparam>
    /// <typeparam name="TLifecycleHookFactory">The concrete lifecycle hook factory type.</typeparam>
    /// <param name="builder">The effect builder.</param>
    /// <param name="factory">The factory instance to register.</param>
    /// <param name="toggleable">Whether this hook can be toggled on/off at runtime. Defaults to <c>true</c>.</param>
    /// <returns>The effect builder for chaining.</returns>
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

    /// <summary>
    /// Registers a train lifecycle hook using an existing factory instance.
    /// Lifecycle hooks run at train start/completion/failure boundaries.
    /// </summary>
    /// <typeparam name="TLifecycleHookFactory">The concrete lifecycle hook factory type.</typeparam>
    /// <param name="builder">The effect builder.</param>
    /// <param name="factory">The factory instance to register.</param>
    /// <param name="toggleable">Whether this hook can be toggled on/off at runtime. Defaults to <c>true</c>.</param>
    /// <returns>The effect builder for chaining.</returns>
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

    #region JunctionInjection

    /// <summary>
    /// Registers a Trax junction with scoped lifetime. The junction's <c>CanonicalName</c> is set to
    /// <typeparamref name="TService"/>'s FullName, and <see cref="InjectAttribute"/> properties are populated.
    /// </summary>
    /// <typeparam name="TService">The junction interface type (used as the canonical name).</typeparam>
    /// <typeparam name="TImplementation">The concrete junction implementation.</typeparam>
    public static IServiceCollection AddScopedTraxJunction<TService, TImplementation>(
        this IServiceCollection services
    )
        where TService : class
        where TImplementation : class, TService =>
        services.AddScopedTraxRoute<TService, TImplementation>();

    /// <summary>
    /// Registers a Trax junction with scoped lifetime using runtime types.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="serviceInterface">The junction interface type (used as the canonical name).</param>
    /// <param name="serviceImplementation">The concrete junction implementation type.</param>
    public static IServiceCollection AddScopedTraxJunction(
        this IServiceCollection services,
        Type serviceInterface,
        Type serviceImplementation
    ) => services.AddScopedTraxRoute(serviceInterface, serviceImplementation);

    /// <summary>
    /// Registers a Trax junction with transient lifetime. The junction's <c>CanonicalName</c> is set to
    /// <typeparamref name="TService"/>'s FullName, and <see cref="InjectAttribute"/> properties are populated.
    /// </summary>
    /// <typeparam name="TService">The junction interface type (used as the canonical name).</typeparam>
    /// <typeparam name="TImplementation">The concrete junction implementation.</typeparam>
    public static IServiceCollection AddTransientTraxJunction<TService, TImplementation>(
        this IServiceCollection services
    )
        where TService : class
        where TImplementation : class, TService =>
        services.AddTransientTraxRoute<TService, TImplementation>();

    /// <summary>
    /// Registers a Trax junction with transient lifetime using runtime types.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="serviceInterface">The junction interface type (used as the canonical name).</param>
    /// <param name="serviceImplementation">The concrete junction implementation type.</param>
    public static IServiceCollection AddTransientTraxJunction(
        this IServiceCollection services,
        Type serviceInterface,
        Type serviceImplementation
    ) => services.AddTransientTraxRoute(serviceInterface, serviceImplementation);

    /// <summary>
    /// Registers a Trax junction with singleton lifetime. The junction's <c>CanonicalName</c> is set to
    /// <typeparamref name="TService"/>'s FullName, and <see cref="InjectAttribute"/> properties are populated.
    /// </summary>
    /// <typeparam name="TService">The junction interface type (used as the canonical name).</typeparam>
    /// <typeparam name="TImplementation">The concrete junction implementation.</typeparam>
    public static IServiceCollection AddSingletonTraxJunction<TService, TImplementation>(
        this IServiceCollection services
    )
        where TService : class
        where TImplementation : class, TService =>
        services.AddSingletonTraxRoute<TService, TImplementation>();

    /// <summary>
    /// Registers a Trax junction with singleton lifetime using runtime types.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="serviceInterface">The junction interface type (used as the canonical name).</param>
    /// <param name="serviceImplementation">The concrete junction implementation type.</param>
    public static IServiceCollection AddSingletonTraxJunction(
        this IServiceCollection services,
        Type serviceInterface,
        Type serviceImplementation
    ) => services.AddSingletonTraxRoute(serviceInterface, serviceImplementation);

    #endregion

    #region RouteInjection

    /// <summary>
    /// Registers a Trax route (train or junction) with scoped lifetime. Sets <c>CanonicalName</c> to
    /// <typeparamref name="TService"/>'s FullName and injects <see cref="InjectAttribute"/> properties.
    /// </summary>
    /// <typeparam name="TService">The route interface type (used as the canonical name).</typeparam>
    /// <typeparam name="TImplementation">The concrete route implementation.</typeparam>
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
            instance
                .GetType()
                .GetProperty("CanonicalName")
                ?.SetValue(instance, typeof(TService).FullName);
            return instance;
        });

        return services;
    }

    /// <summary>
    /// Registers a Trax route with scoped lifetime using runtime types.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="serviceInterface">The route interface type (used as the canonical name).</param>
    /// <param name="serviceImplementation">The concrete route implementation type.</param>
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
                instance
                    .GetType()
                    .GetProperty("CanonicalName")
                    ?.SetValue(instance, serviceInterface.FullName);
                return instance;
            }
        );

        return services;
    }

    /// <summary>
    /// Registers a Trax route with transient lifetime. Sets <c>CanonicalName</c> to
    /// <typeparamref name="TService"/>'s FullName and injects <see cref="InjectAttribute"/> properties.
    /// </summary>
    /// <typeparam name="TService">The route interface type (used as the canonical name).</typeparam>
    /// <typeparam name="TImplementation">The concrete route implementation.</typeparam>
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
            instance
                .GetType()
                .GetProperty("CanonicalName")
                ?.SetValue(instance, typeof(TService).FullName);
            return instance;
        });

        return services;
    }

    /// <summary>
    /// Registers a Trax route with transient lifetime using runtime types.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="serviceInterface">The route interface type (used as the canonical name).</param>
    /// <param name="serviceImplementation">The concrete route implementation type.</param>
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
                instance
                    .GetType()
                    .GetProperty("CanonicalName")
                    ?.SetValue(instance, serviceInterface.FullName);
                return instance;
            }
        );

        return services;
    }

    /// <summary>
    /// Registers a Trax route with singleton lifetime. Sets <c>CanonicalName</c> to
    /// <typeparamref name="TService"/>'s FullName and injects <see cref="InjectAttribute"/> properties.
    /// </summary>
    /// <typeparam name="TService">The route interface type (used as the canonical name).</typeparam>
    /// <typeparam name="TImplementation">The concrete route implementation.</typeparam>
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
            instance
                .GetType()
                .GetProperty("CanonicalName")
                ?.SetValue(instance, typeof(TService).FullName);
            return instance;
        });

        return services;
    }

    /// <summary>
    /// Registers a Trax route with singleton lifetime using runtime types.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="serviceInterface">The route interface type (used as the canonical name).</param>
    /// <param name="serviceImplementation">The concrete route implementation type.</param>
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
                instance
                    .GetType()
                    .GetProperty("CanonicalName")
                    ?.SetValue(instance, serviceInterface.FullName);
                return instance;
            }
        );

        return services;
    }

    #endregion
}
