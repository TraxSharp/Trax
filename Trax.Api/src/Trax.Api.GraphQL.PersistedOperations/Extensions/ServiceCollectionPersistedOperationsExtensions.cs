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
    public static IServiceCollection AddPersistedOperationStore(
        this IServiceCollection services,
        string databaseConnectionString
    )
    {
        ArgumentNullException.ThrowIfNull(services);
        if (string.IsNullOrWhiteSpace(databaseConnectionString))
            throw new ArgumentException(
                "AddPersistedOperationStore requires a connection string.",
                nameof(databaseConnectionString)
            );

        var options = new PersistedOperationsBuilder()
            .UseDatabase(databaseConnectionString)
            .Build();
        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);

        services.TryAddSingleton<IPersistedOperationCache, NoOpPersistedOperationCache>();
        services.TryAddSingleton<
            IPersistedOperationBroadcaster,
            NoOpPersistedOperationBroadcaster
        >();
        services.TryAddSingleton<IPersistedOperationValidator, NoOpPersistedOperationValidator>();

        services.AddSingleton<DbPersistedOperationStorage>();
        services.AddSingleton<IPersistedOperationStore>(sp =>
            sp.GetRequiredService<DbPersistedOperationStorage>()
        );

        return services;
    }
}
