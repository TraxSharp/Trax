using System.ComponentModel;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Execution.Caching;
using HotChocolate.Execution.Configuration;
using HotChocolate.Features;
using HotChocolate.Language;
using HotChocolate.PersistedOperations;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
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
    /// <para>
    /// This exposes the operations namespace, including the persisted-operation management
    /// mutations. Those are admin operations, so the host must gate them: <c>GateOperations(...)</c>
    /// to gate the namespace while the rest of the endpoint stays open,
    /// <c>RequireAuthorization()</c> to gate the whole endpoint, or
    /// <c>AllowAnonymousOperations()</c> to explicitly opt into anonymous access. Otherwise
    /// <c>AddTraxGraphQL()</c> fails at startup. A host that wants enforcement without the
    /// namespace calls <c>ExposeOperationsNamespace(false)</c> and answers none of this.
    /// </para>
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

        // Which boundary is in front of this endpoint is invisible in a running system: the schema
        // is the same either way. Say it once at startup so it can be checked where it is claimed.
        services.AddHostedService<PersistedOperations.Startup.PersistedOperationsEnforcementReporter>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<PersistedOperationCacheGeneration>();

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
        //
        // Declinable: the type extensions are registered only alongside the namespace they
        // extend, because an [ExtendObjectType(typeof(OperationsMutations))] class whose target
        // type is not in the schema is a build error, not a no-op.
        if (options.ExposeOperationsNamespace)
        {
            builder.ExposeOperationQueries();
            builder.ExposeOperationMutations();
            builder.AddTypeExtensions(typeof(GraphQL.PersistedOperationMutations).Assembly);
        }

        // HotChocolate cache invalidator. The schema name is captured below
        // inside ConfigureSchema so the invalidator can evict the right executor.
        services.AddSingleton<HotChocolateOperationCacheInvalidator>();

        // Storage: implements both IPersistedOperationStore and the HC hot-path.
        services.AddSingleton<DbPersistedOperationStorage>();
        services.AddSingleton<IPersistedOperationStore>(sp =>
            sp.GetRequiredService<DbPersistedOperationStorage>()
        );
        services.TryAddSingleton<
            Services.IPersistedOperationsService,
            Services.PersistedOperationsService
        >();

        // Allowlist matcher and the decision the enforcement middleware asks.
        services.AddSingleton<AllowlistMatcher>();
        services.AddSingleton<PersistedOperationPolicy>();

        // HotChocolate's persisted-operation middleware resolves
        // IOperationDocumentStorage from the schema-scoped service provider.
        // Register it on the schema services so the request executor finds it.
        builder.ConfigureSchema(schema =>
        {
            // HC's schema-services container does not forward to root. HotChocolate 16
            // replaced IApplicationServiceProvider with an explicit bridge:
            // AddApplicationService makes a root-container registration resolvable from
            // the schema-scoped provider the persisted-operation middleware uses.
            schema.AddApplicationService<DbPersistedOperationStorage>();
            schema.AddApplicationService<HotChocolateOperationCacheInvalidator>();

            // Substitute caches that can be emptied and that stamp their entries. HotChocolate's
            // own document and prepared-operation caches assume a persisted-operation id maps to
            // one document forever: they expose no way to drop an entry and never expire, which
            // is exactly what re-uploading or deactivating a document under an existing id needs.
            // Both are sized from the host's schema options, as HotChocolate sizes its own.
            // See docs/adr/0034-a-cached-persisted-operation-is-never-older-than-the-last-change-or-its-maximum-age.md.
            //
            // HotChocolate keeps the document cache as an application service keyed by schema
            // name (TryAdd), and every AddGraphQL call for the schema forwards the schema's
            // IDocumentCache to it. Registering Trax's under the same key, with Add, makes it the
            // one every forward resolves, whatever order the host calls AddGraphQL in.
            var schemaName = schema.Name;
            schema.Services.AddKeyedSingleton<IDocumentCache>(
                schemaName,
                (applicationServices, _) =>
                    new ClearableDocumentCache(
                        HostSchemaOptions(
                            applicationServices,
                            schemaName
                        ).OperationDocumentCacheSize,
                        applicationServices.GetRequiredService<PersistedOperationCacheGeneration>(),
                        applicationServices.GetRequiredService<TimeProvider>(),
                        options.CacheMaxAge
                    )
            );
            schema.ConfigureSchemaServices(
                (applicationServices, sc) =>
                {
                    sc.AddSingleton(_ =>
                        applicationServices.GetRequiredKeyedService<IDocumentCache>(schemaName)
                    );
                    sc.AddSingleton<IPreparedOperationCache>(
                        schemaServices => new ClearablePreparedOperationCache(
                            schemaServices
                                .GetRequiredService<ISchemaDefinition>()
                                .Features.GetRequired<IReadOnlySchemaOptions>()
                                .PreparedOperationCacheSize,
                            applicationServices.GetRequiredService<PersistedOperationCacheGeneration>(),
                            applicationServices.GetRequiredService<TimeProvider>(),
                            options.CacheMaxAge
                        )
                    );
                }
            );

            schema.ConfigureSchemaServices(sc =>
                sc.AddSingleton<IOperationDocumentStorage>(sp =>
                {
                    // Capture the schema name on the invalidator the first
                    // time the schema services are built. ConfigureSchemaServices
                    // runs lazily during executor build, by which time the root
                    // provider has the singleton ready.
                    sp.GetRequiredService<HotChocolateOperationCacheInvalidator>()
                        .SetSchemaName(schema.Name);
                    RequireTraxCaches(sp);
                    return sp.GetRequiredService<DbPersistedOperationStorage>();
                })
            );
            schema.UsePersistedOperationPipeline();
            // Every cache the request fills is stamped with the generation it started in. This
            // wraps the document cache, the persisted-operation read and the operation cache.
            schema.UseRequest(
                PersistedOperationCacheScopeMiddleware.Create,
                key: PersistedOperationCacheScopeMiddleware.Key,
                before: WellKnownRequestMiddleware.DocumentCacheMiddleware
            );
            // A persisted-operation id runs only the document the store holds for it. This runs
            // before the document cache is consulted, so a document sent alongside an id is never
            // bound to it. See
            // docs/adr/0026-a-persisted-operation-id-means-one-document-on-every-node.md.
            schema.UseRequest(
                PersistedOperationIdBindingMiddleware.Create,
                key: PersistedOperationIdBindingMiddleware.Key,
                before: WellKnownRequestMiddleware.DocumentCacheMiddleware
            );
            // Enforcement runs here, after the document is parsed and before it is validated, so
            // every transport that reaches the executor gets the same decision. See
            // docs/adr/0013-persisted-operation-enforcement-runs-in-the-execution-pipeline.md.
            schema.UseRequest(
                PersistedOperationEnforcementMiddleware.Create,
                key: PersistedOperationEnforcementMiddleware.Key,
                after: WellKnownRequestMiddleware.DocumentParserMiddleware
            );
        });

        return builder;
    }

    /// <summary>
    /// The schema options the host configured (<c>ModifyOptions</c>), read the way HotChocolate
    /// reads them to size its own document cache.
    /// </summary>
    private static SchemaOptions HostSchemaOptions(
        IServiceProvider applicationServices,
        string schemaName
    )
    {
        var setup = applicationServices
            .GetRequiredService<IOptionsMonitor<RequestExecutorSetup>>()
            .Get(schemaName);
        var schemaOptions = new SchemaOptions();
        foreach (var modify in setup.SchemaOptionModifiers)
            modify(schemaOptions);
        return schemaOptions;
    }

    /// <summary>
    /// Refuses to build the executor when something replaced Trax's caches: HotChocolate's own
    /// cannot be emptied and never expire, so a change to a persisted operation would not be seen.
    /// </summary>
    private static void RequireTraxCaches(IServiceProvider schemaServices)
    {
        if (
            schemaServices.GetRequiredService<IDocumentCache>() is not ClearableDocumentCache
            || schemaServices.GetRequiredService<IPreparedOperationCache>()
                is not ClearablePreparedOperationCache
        )
            throw new InvalidOperationException(
                "Persisted operations need Trax's document and prepared-operation caches, which "
                    + "a change to a persisted operation can empty, and something replaced them. "
                    + "Remove the registration of IDocumentCache or IPreparedOperationCache that "
                    + "follows UsePersistedOperations."
            );
    }

    /// <summary>
    /// Kept so existing hosts compile. Enforcement no longer needs it: <c>UsePersistedOperations</c>
    /// enforces inside HotChocolate's execution pipeline, which every transport passes through,
    /// so this adds nothing to the ASP.NET pipeline.
    /// </summary>
    /// <remarks>
    /// See <c>docs/adr/0013-persisted-operation-enforcement-runs-in-the-execution-pipeline.md</c>.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static IApplicationBuilder UsePersistedOperationsEnforcement(
        this IApplicationBuilder app
    )
    {
        ArgumentNullException.ThrowIfNull(app);
        return app;
    }
}
