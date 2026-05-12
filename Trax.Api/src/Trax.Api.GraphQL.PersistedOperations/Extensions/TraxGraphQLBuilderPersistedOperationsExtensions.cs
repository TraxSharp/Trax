using HotChocolate.Execution;
using HotChocolate.Execution.Configuration;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.PersistedOperations.Broadcasting;
using Trax.Api.GraphQL.PersistedOperations.Configuration;
using Trax.Api.GraphQL.PersistedOperations.Middleware;
using Trax.Api.GraphQL.PersistedOperations.Storage;
using Trax.Api.GraphQL.PersistedOperations.Storage.Validation;

namespace Trax.Api.GraphQL.PersistedOperations.Extensions;

/// <summary>
/// Public entry point. Adds persisted GraphQL operations to a Trax GraphQL
/// pipeline.
/// </summary>
public static class TraxGraphQLBuilderPersistedOperationsExtensions
{
    /// <summary>
    /// Wires persisted-operations enforcement, storage, optional cache, and
    /// optional cross-node invalidation. Call once during service registration.
    /// </summary>
    /// <example>
    /// <code>
    /// builder.Services.AddTraxGraphQL(graphql => graphql
    ///     .AddDbContext&lt;ClientDataContext&gt;()
    ///     .UsePersistedOperations(opts => opts
    ///         .RequirePersisted(true)
    ///         .LogNonPersistedRequests(true)
    ///         .AllowOperationsMatching(id => id.StartsWith("dev_"))
    ///     )
    /// );
    /// </code>
    /// </example>
    /// <remarks>
    /// Storage uses the existing Trax <c>IDataContextProviderFactory</c>
    /// registered by <c>AddEffects(...).UsePostgres(...)</c>. The persisted-
    /// operation tables (<c>trax.persisted_operation</c>,
    /// <c>trax.persisted_operation_history</c>) live in the same <c>trax</c>
    /// schema as the rest of the Trax tables.
    /// </remarks>
    public static TraxGraphQLBuilder UsePersistedOperations(
        this TraxGraphQLBuilder builder,
        Action<PersistedOperationsBuilder> configure
    )
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        var poBuilder = new PersistedOperationsBuilder();
        configure(poBuilder);
        var options = poBuilder.Build();

        var services = builder.Services;

        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);

        // Cache: in-memory wrapper if enabled, else no-op.
        if (options.CacheEnabled)
        {
            services.AddMemoryCache();
            services.AddSingleton<IPersistedOperationCache, InMemoryPersistedOperationCache>();
        }
        else
        {
            services.AddSingleton<IPersistedOperationCache, NoOpPersistedOperationCache>();
        }

        // Broadcaster: RabbitMQ if configured, else no-op.
        if (!string.IsNullOrEmpty(options.RabbitMqConnectionString))
        {
            services.AddSingleton<
                IPersistedOperationBroadcaster,
                RabbitMqPersistedOperationBroadcaster
            >();
            services.AddSingleton<PersistedOperationReceiverService>();
            services.AddHostedService(sp =>
                sp.GetRequiredService<PersistedOperationReceiverService>()
            );
        }
        else
        {
            services.AddSingleton<
                IPersistedOperationBroadcaster,
                NoOpPersistedOperationBroadcaster
            >();
        }

        // Validator: HotChocolate-backed in this path (we have a schema in process).
        // Replace overrides the no-op default from AddPersistedOperationStore if it
        // was also called.
        services.Replace(
            ServiceDescriptor.Singleton<IPersistedOperationValidator>(
                sp => new HotChocolateSchemaValidator(sp)
            )
        );

        // Capability marker: presence in DI signals to consumers (dashboard)
        // that the full persisted-operations subsystem is wired in.
        services.AddSingleton<IPersistedOperationsCapability, PersistedOperationsCapability>();

        // Management mutations + queries. Scanned via the existing
        // TraxGraphQLBuilder.AddTypeExtensions helper. Also flip the
        // operations-exposed flags so AddTraxGraphQL emits the OperationsQueries
        // and OperationsMutations namespaces that our type extensions graft onto.
        builder.ExposeOperationQueries();
        builder.ExposeOperationMutations();
        builder.AddTypeExtensions(typeof(GraphQL.PersistedOperationMutations).Assembly);

        // HotChocolate cache invalidator. The schema name is captured below
        // inside ConfigureSchema so the invalidator can resolve the right
        // executor when clearing IPreparedOperationCache.
        services.AddSingleton<HotChocolateOperationCacheInvalidator>();

        // Storage: implements both IPersistedOperationStore and the HC hot-path.
        services.AddSingleton<DbPersistedOperationStorage>();
        services.AddSingleton<IPersistedOperationStore>(sp =>
            sp.GetRequiredService<DbPersistedOperationStorage>()
        );

        // Allowlist matcher used by the middleware.
        services.AddSingleton<AllowlistMatcher>();

        // HotChocolate's persisted-operation middleware resolves
        // IOperationDocumentStorage from the schema-scoped service provider.
        // Register it on the schema services so the request executor finds it.
        builder.ConfigureSchema(schema =>
        {
            // Storage resolves through HC's schema-services container, which
            // does not forward to root. Use IApplicationServiceProvider to
            // reach back into the root container where DbPersistedOperationStorage
            // is registered.
            schema.ConfigureSchemaServices(sc =>
                sc.AddSingleton<IOperationDocumentStorage>(sp =>
                {
                    var root = sp.GetRequiredService<HotChocolate.IApplicationServiceProvider>();
                    // Capture the schema name on the invalidator the first
                    // time the schema services are built. ConfigureSchemaServices
                    // runs lazily during executor build, by which time the root
                    // provider has the singleton ready.
                    root.GetRequiredService<HotChocolateOperationCacheInvalidator>()
                        .SetSchemaName(schema.Name);
                    return root.GetRequiredService<DbPersistedOperationStorage>();
                })
            );
            schema.UsePersistedOperationPipeline();
            schema.ModifyRequestOptions(opts =>
            {
                opts.PersistedOperations.AllowDocumentBody = true;
            });
        });

        return builder;
    }

    /// <summary>
    /// Inserts the persisted-operations enforcement middleware into the
    /// ASP.NET pipeline. Call AFTER <c>UseRouting()</c> and BEFORE
    /// <c>UseTraxGraphQL()</c>.
    /// </summary>
    public static IApplicationBuilder UsePersistedOperationsEnforcement(
        this IApplicationBuilder app
    )
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.UseMiddleware<PersistedOperationsMiddleware>();
    }
}
