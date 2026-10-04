using System.ComponentModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Trax.Api.GraphQL.PersistedOperations.Broadcasting;
using Trax.Api.GraphQL.PersistedOperations.Configuration;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Api.GraphQL.PersistedOperations.Storage.Validation;

namespace Trax.Api.GraphQL.PersistedOperations.Extensions;

/// <summary>
/// Standalone DI extension for non-GraphQL hosts that need
/// <see cref="IPersistedOperationStore"/> (admin tooling, manifest
/// uploaders, console clients). Assumes the consumer has already
/// registered the Trax data layer (via <c>AddTrax(t =&gt; t.AddEffects(e =&gt;
/// e.UsePostgres(...)))</c>) so that <c>IDataContextProviderFactory</c> is
/// resolvable.
/// </summary>
public static class ServiceCollectionPersistedOperationsExtensions
{
    /// <summary>
    /// Registers <see cref="IPersistedOperationStore"/> backed by the Trax data context. Use this
    /// in admin tools and CI manifest uploaders that do not host a GraphQL server.
    /// </summary>
    /// <remarks>
    /// The store refuses to start until <paramref name="configure"/> says how a change made
    /// through it reaches the GraphQL nodes: <c>UseRabbitMqInvalidation(...)</c> with the broker
    /// they use, or <c>SingleNode()</c> when no other process caches these operations.
    /// </remarks>
    /// <example>
    /// <code>
    /// services.AddPersistedOperationStore(store =&gt; store.UseRabbitMqInvalidation(rabbitMq));
    /// </code>
    /// </example>
    public static IServiceCollection AddPersistedOperationStore(
        this IServiceCollection services,
        Action<PersistedOperationStoreBuilder> configure
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        var storeBuilder = new PersistedOperationStoreBuilder();
        configure(storeBuilder);
        return AddStore(services, storeBuilder.Build());
    }

    /// <summary>
    /// Refuses to start: a store registered this way has no way to reach the GraphQL nodes.
    /// Use <c>AddPersistedOperationStore(store =&gt; ...)</c>.
    /// </summary>
    [Obsolete(
        "A persisted-operation store must say how a change reaches the GraphQL nodes. Use "
            + "AddPersistedOperationStore(store => store.UseRabbitMqInvalidation(...)) or "
            + "AddPersistedOperationStore(store => store.SingleNode()).",
        error: true
    )]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IServiceCollection AddPersistedOperationStore(
        this IServiceCollection services,
        string databaseConnectionString
    ) => AddPersistedOperationStore(services, _ => { });

    /// <summary>
    /// Kept for hosts built against it. The database connection string is not used: the store
    /// reads and writes through the Trax data context.
    /// </summary>
    [Obsolete(
        "The database connection string is not used: the store reads and writes through the Trax "
            + "data context. Use AddPersistedOperationStore(store => store.UseRabbitMqInvalidation(...))."
    )]
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IServiceCollection AddPersistedOperationStore(
        this IServiceCollection services,
        string databaseConnectionString,
        string rabbitMqInvalidationConnectionString
    ) =>
        AddPersistedOperationStore(
            services,
            store => store.UseRabbitMqInvalidation(rabbitMqInvalidationConnectionString)
        );

    private static IServiceCollection AddStore(
        IServiceCollection services,
        string? rabbitMqInvalidationConnectionString
    )
    {
        var builder = new PersistedOperationsBuilder();
        if (rabbitMqInvalidationConnectionString is null)
            builder.SingleNode();
        else
            builder.UseRabbitMqInvalidation(rabbitMqInvalidationConnectionString);

        services.AddSingleton(builder.Build());
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<PersistedOperationCacheGeneration>();

        services.TryAddSingleton<IPersistedOperationCache, NoOpPersistedOperationCache>();
        if (rabbitMqInvalidationConnectionString is not null)
            services.TryAddSingleton<
                IPersistedOperationBroadcaster,
                RabbitMqPersistedOperationBroadcaster
            >();
        else
            services.TryAddSingleton<
                IPersistedOperationBroadcaster,
                NoOpPersistedOperationBroadcaster
            >();
        services.TryAddSingleton<IPersistedOperationValidator, NoOpPersistedOperationValidator>();

        // The storage empties HotChocolate's operation caches after a write. With no GraphQL
        // server in this container there is no executor, and the invalidator does nothing.
        services.TryAddSingleton<HotChocolateOperationCacheInvalidator>();

        services.AddSingleton<DbPersistedOperationStorage>();
        services.AddSingleton<IPersistedOperationStore>(sp =>
            sp.GetRequiredService<DbPersistedOperationStorage>()
        );
        services.TryAddSingleton<
            Services.IPersistedOperationsService,
            Services.PersistedOperationsService
        >();

        return services;
    }
}
