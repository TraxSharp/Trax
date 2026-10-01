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
    /// Registers <see cref="IPersistedOperationStore"/> backed by the
    /// existing Trax data context. Use this in admin tools and CI manifest
    /// uploaders that do not host a GraphQL server.
    /// </summary>
    /// <remarks>
    /// A change written through this store is not broadcast, so a GraphQL node keeps serving what
    /// it cached until it restarts. When the nodes use <c>UseRabbitMqInvalidation</c>, use the
    /// overload that takes the broker's connection string, so a change made here reaches them.
    /// </remarks>
    public static IServiceCollection AddPersistedOperationStore(
        this IServiceCollection services,
        string databaseConnectionString
    ) => AddStore(services, databaseConnectionString, rabbitMqInvalidationConnectionString: null);

    /// <summary>
    /// Registers <see cref="IPersistedOperationStore"/> backed by the existing Trax data context,
    /// and broadcasts every change it makes over RabbitMQ to the GraphQL nodes that call
    /// <c>UseRabbitMqInvalidation</c> with the same broker, so each of them empties its caches.
    /// </summary>
    public static IServiceCollection AddPersistedOperationStore(
        this IServiceCollection services,
        string databaseConnectionString,
        string rabbitMqInvalidationConnectionString
    )
    {
        if (string.IsNullOrWhiteSpace(rabbitMqInvalidationConnectionString))
            throw new ArgumentException(
                "AddPersistedOperationStore requires a RabbitMQ connection string in this overload.",
                nameof(rabbitMqInvalidationConnectionString)
            );

        return AddStore(services, databaseConnectionString, rabbitMqInvalidationConnectionString);
    }

    private static IServiceCollection AddStore(
        IServiceCollection services,
        string databaseConnectionString,
        string? rabbitMqInvalidationConnectionString
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        if (string.IsNullOrWhiteSpace(databaseConnectionString))
            throw new ArgumentException(
                "AddPersistedOperationStore requires a connection string.",
                nameof(databaseConnectionString)
            );

        var builder = new PersistedOperationsBuilder().UseDatabase(databaseConnectionString);
        if (rabbitMqInvalidationConnectionString is not null)
            builder.UseRabbitMqInvalidation(rabbitMqInvalidationConnectionString);

        // This process serves no requests, so it has no caches of its own to keep consistent.
        var options = builder.Build(requireNodeTopology: false);
        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);

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
