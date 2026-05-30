using System.Reflection;
using HotChocolate.Data;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using HotChocolate.Validation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Authorization;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Errors;
using Trax.Api.GraphQL.Hooks;
using Trax.Api.GraphQL.Mutations;
using Trax.Api.GraphQL.Queries;
using Trax.Api.GraphQL.Startup;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Api.GraphQL.TypeModules;
using Trax.Api.GraphQL.Types;
using Trax.Api.GraphQL.Validation;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.TrainEventBroadcaster;
using Trax.Effect.Services.TrainLifecycleHookFactory;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.GraphQL.Extensions;

public static class GraphQLServiceExtensions
{
    private const string SchemaName = "trax";

    /// <summary>
    /// Cached open-generic <c>SchemaRequestExecutorBuilderExtensions.AddTypeModule&lt;T&gt;</c>
    /// method for registering consumer-provided TypeModules at runtime.
    /// </summary>
    private static readonly MethodInfo AddTypeModuleMethod =
        typeof(SchemaRequestExecutorBuilderExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m =>
                m.Name == "AddTypeModule"
                && m.IsGenericMethodDefinition
                && m.GetParameters().Length == 1
                && m.GetParameters()[0].ParameterType == typeof(IRequestExecutorBuilder)
            );

    /// <summary>
    /// Cached open-generic <c>SchemaRequestExecutorBuilderExtensions.AddTypeExtension&lt;T&gt;</c>
    /// method for registering consumer-provided type extensions at runtime.
    /// </summary>
    private static readonly MethodInfo AddTypeExtensionMethod =
        typeof(SchemaRequestExecutorBuilderExtensions)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m =>
                m.Name == "AddTypeExtension"
                && m.IsGenericMethodDefinition
                && m.GetGenericArguments().Length == 1
                && m.GetParameters().Length == 1
                && m.GetParameters()[0].ParameterType == typeof(IRequestExecutorBuilder)
            );

    /// <summary>
    /// Registers the Trax GraphQL schema on a named HotChocolate server ("trax")
    /// with support for configuring DbContext-based model queries.
    /// </summary>
    /// <example>
    /// <code>
    /// services.AddTraxGraphQL(graphql => graphql
    ///     .AddDbContext&lt;GameDbContext&gt;());
    /// </code>
    /// </example>
    public static IServiceCollection AddTraxGraphQL(
        this IServiceCollection services,
        Func<TraxGraphQLBuilder, TraxGraphQLBuilder> configure
    )
    {
        if (!services.Any(sd => sd.ServiceType == typeof(TraxMarker)))
            throw new InvalidOperationException(
                "AddTraxGraphQL() requires AddTrax() to be called first. "
                    + "Call services.AddTrax(trax => ...) before services.AddTraxGraphQL()."
            );

        var builder = new TraxGraphQLBuilder(services);
        configure(builder);
        var config = builder.Build();
        services.AddSingleton(config);

        services.AddTraxApi();
        services.AddSingleton<TrainTypeModule>();
        services.AddTransient<GraphQLSubscriptionHook>();
        services
            .AddSingleton<LifecycleHookFactory<GraphQLSubscriptionHook>>()
            .AddSingleton<ITrainLifecycleHookFactory>(sp =>
                sp.GetRequiredService<LifecycleHookFactory<GraphQLSubscriptionHook>>()
            );

        // Detect train queries/mutations registered before us so we can decide whether
        // RootQuery / RootMutation will have any fields by the time HotChocolate builds
        // the schema. Trains registered AFTER AddTraxGraphQL won't be picked up here —
        // the established pattern is `AddTrax(...).AddTraxGraphQL(...)` with all train
        // registrations completed inside or before AddTrax.
        // Honor a pre-registered ITrainDiscoveryService when present (test setups
        // substitute a mock), otherwise scan the live ServiceCollection ourselves.
        var trainRegistrations = ResolveTrainDiscoveryService(services).DiscoverTrains();
        var hasTrainQueries = trainRegistrations.Any(r => r.IsQuery);
        var hasTrainMutations = trainRegistrations.Any(r => r.IsMutation);

        var hasQueryRoot =
            config.OperationQueriesExposed
            || hasTrainQueries
            || config.ModelRegistrations.Count > 0;
        var hasMutationRoot = config.OperationMutationsExposed || hasTrainMutations;

        if (!hasQueryRoot)
            throw new InvalidOperationException(
                "AddTraxGraphQL() found no GraphQL queries to expose. The root Query type "
                    + "would be empty and HotChocolate would fail to build the schema. "
                    + "Either register at least one [TraxQuery] train, register a "
                    + "DbContext via AddDbContext<T>() with [TraxQueryModel] entities, or "
                    + "call ExposeOperationQueries() on the builder to expose the "
                    + "predefined operations namespace."
            );

        var graphqlBuilder = services.AddGraphQLServer(SchemaName);

        graphqlBuilder.AddQueryType<RootQuery>();

        if (hasMutationRoot)
            graphqlBuilder.AddMutationType<RootMutation>();

        graphqlBuilder
            .AddSubscriptionType<LifecycleSubscriptions>()
            .AddType<TrainLifecycleEventType>()
            .AddTypeModule<TrainTypeModule>()
            .AddErrorFilter<TraxErrorFilter>()
            .AddInMemorySubscriptions();

        if (config.OperationQueriesExposed)
        {
            graphqlBuilder.AddType(new ObjectType<OperationsQueries>());
            graphqlBuilder.AddType(new ObjectType<DeadLetterQueries>());
            graphqlBuilder.AddType(new ObjectType<WorkQueueQueries>());
            graphqlBuilder.AddType(new ObjectType<ManifestGroupQueries>());
            graphqlBuilder.AddType(new ObjectType<LogQueries>());
            graphqlBuilder.AddType(new ObjectType<MetricsQueries>());
            graphqlBuilder.AddType(new ObjectType<ConfigQueries>());
            graphqlBuilder.AddTypeExtension(
                new ObjectTypeExtension(d =>
                {
                    d.Name("RootQuery");
                    d.Field("operations")
                        .Type<ObjectType<OperationsQueries>>()
                        .Resolve(_ => new OperationsQueries());
                })
            );
        }

        if (config.OperationMutationsExposed)
        {
            graphqlBuilder.AddType(new ObjectType<OperationsMutations>());
            graphqlBuilder.AddType(new ObjectType<DeadLetterMutations>());
            graphqlBuilder.AddType(new ObjectType<WorkQueueMutations>());
            graphqlBuilder.AddType(new ObjectType<ManifestGroupMutations>());
            graphqlBuilder.AddType(new ObjectType<ConfigMutations>());
            graphqlBuilder.AddTypeExtension(
                new ObjectTypeExtension(d =>
                {
                    d.Name("RootMutation");
                    d.Field("operations")
                        .Type<ObjectType<OperationsMutations>>()
                        .Resolve(_ => new OperationsMutations());
                })
            );
        }

        ApplyHardeningDefaults(services, graphqlBuilder, config);

        if (config.ModelRegistrations.Count > 0)
        {
            services.AddSingleton<QueryModelTypeModule>();
            graphqlBuilder.AddTypeModule<QueryModelTypeModule>();

            // Wire HotChocolate's @authorize directive handler whenever any
            // model entity carries [TraxAuthorize]. The directive runs against
            // ASP.NET Core's IAuthorizationService, so RequireRole / policy
            // definitions registered via services.AddAuthorization(...) apply.
            // Wiring is conditional so the dependency is opt-in for hosts that
            // expose no gated models (the directive handler pulls in ASP.NET
            // Core authorization machinery).
            var hasGated = config.ModelRegistrations.Any(r => r.AuthorizeAttributes.Count > 0);
            var hasAnonymous = config.ModelRegistrations.Any(r => r.AllowAnonymous);

            if (hasGated)
            {
                graphqlBuilder.AddAuthorization();
                graphqlBuilder.AddHttpRequestInterceptor<QueryModelAuthenticationInterceptor>();
                services.AddHostedService<QueryModelAuthorizationValidator>();
            }

            // Schema validator covers both positive (gated has @authorize) and
            // inverse ([TraxAllowAnonymous] has no @authorize) invariants. Run
            // it whenever either flavor of entity is present so a stray
            // ConfigureSchema callback can be caught at host start in either
            // direction.
            if (hasGated || hasAnonymous)
            {
                services.AddHostedService<QueryModelAuthorizationSchemaValidator>();
            }

            // Register DiscoverQueries base type and discover field on RootQuery.
            // TrainTypeModule will skip creating these when it detects model registrations.
            graphqlBuilder.AddType(new ObjectType<DiscoverQueries>());
            graphqlBuilder.AddTypeExtension(
                new ObjectTypeExtension(d =>
                {
                    d.Name("RootQuery");
                    d.Field("discover")
                        .Type<ObjectType<DiscoverQueries>>()
                        .Resolve(_ => new DiscoverQueries());
                })
            );

            if (config.ModelRegistrations.Any(r => r.Attribute.Filtering))
            {
                if (config.FilterModules.Count > 0)
                    graphqlBuilder.AddFiltering(convention =>
                    {
                        // Supplying a configure action replaces HotChocolate's default
                        // convention wiring, so re-establish the stock operations and
                        // queryable provider before layering the opt-in modules on top.
                        convention.AddDefaults();
                        foreach (var module in config.FilterModules)
                            module.Apply(convention);
                    });
                else
                    graphqlBuilder.AddFiltering();
            }

            if (config.ModelRegistrations.Any(r => r.Attribute.Sorting))
                graphqlBuilder.AddSorting();

            if (config.ModelRegistrations.Any(r => r.Attribute.Projection))
                graphqlBuilder.AddProjections();
        }

        // Register additional TypeModules provided by consumers via AddTypeModule<T>().
        foreach (var typeModuleType in config.AdditionalTypeModules)
        {
            services.AddSingleton(typeModuleType);
            AddTypeModuleMethod.MakeGenericMethod(typeModuleType).Invoke(null, [graphqlBuilder]);
        }

        // Register additional type extensions provided by consumers
        // via AddTypeExtension<T>() or AddTypeExtensions(assembly).
        foreach (var typeExtensionType in config.AdditionalTypeExtensions)
        {
            AddTypeExtensionMethod
                .MakeGenericMethod(typeExtensionType)
                .Invoke(null, [graphqlBuilder]);
        }

        // Apply consumer-provided schema configuration callbacks last,
        // so they can override any standard Trax configuration.
        foreach (var schemaConfiguration in config.SchemaConfigurations)
        {
            schemaConfiguration(graphqlBuilder);
        }

        // If a broadcaster receiver is registered (via UseBroadcaster()),
        // wire up the GraphQL handler so remote lifecycle events are forwarded
        // to HotChocolate subscriptions.
        if (services.Any(sd => sd.ServiceType == typeof(ITrainEventReceiver)))
        {
            services.AddTransient<ITrainEventHandler, GraphQLTrainEventHandler>();
        }

        return services;
    }

    /// <summary>
    /// Registers the Trax GraphQL schema on a named HotChocolate server ("trax").
    /// This avoids conflicts with a consumer's own default GraphQL schema.
    /// Only trains annotated with <c>[TraxQuery]</c> or <c>[TraxMutation]</c> get typed operations generated.
    /// </summary>
    public static IServiceCollection AddTraxGraphQL(this IServiceCollection services) =>
        services.AddTraxGraphQL(builder => builder);

    /// <summary>
    /// Maps the Trax GraphQL endpoint at the specified route prefix.
    /// Uses a named schema so it coexists with other HotChocolate schemas
    /// in the same application. Use the optional <paramref name="configure"/> callback
    /// to apply endpoint conventions such as authorization or rate limiting.
    /// </summary>
    /// <example>
    /// <code>
    /// app.UseTraxGraphQL(configure: endpoint => endpoint
    ///     .RequireAuthorization("AdminPolicy"));
    /// </code>
    /// </example>
    public static WebApplication UseTraxGraphQL(
        this WebApplication app,
        string routePrefix = "/trax/graphql",
        Action<IEndpointConventionBuilder>? configure = null
    )
    {
        app.UseWebSockets();
        var endpoint = app.MapGraphQL(routePrefix, SchemaName);
        configure?.Invoke(endpoint);
        return app;
    }

    /// <summary>
    /// Returns a <see cref="ITrainDiscoveryService"/> that reflects the current
    /// <see cref="IServiceCollection"/> contents. Prefers an instance/factory
    /// registered by the consumer (used by tests that substitute a mock), and
    /// falls back to scanning the live collection when none is registered.
    /// </summary>
    private static ITrainDiscoveryService ResolveTrainDiscoveryService(IServiceCollection services)
    {
        var descriptor = services.LastOrDefault(sd =>
            sd.ServiceType == typeof(ITrainDiscoveryService)
        );
        if (descriptor?.ImplementationInstance is ITrainDiscoveryService instance)
            return instance;

        return new TrainDiscoveryService(services);
    }

    /// <summary>
    /// Applies the Trax GraphQL hardening defaults — max depth, cost analysis,
    /// introspection gating, and per-request operation cap — plus any overrides
    /// the consumer supplied via <c>TraxGraphQLBuilder</c>.
    /// </summary>
    private static void ApplyHardeningDefaults(
        IServiceCollection services,
        IRequestExecutorBuilder graphqlBuilder,
        GraphQLConfiguration config
    )
    {
        // G1 — Max execution depth. Defaults to 15 unless the consumer overrides.
        graphqlBuilder.AddMaxExecutionDepthRule(
            config.MaxExecutionDepth,
            skipIntrospectionFields: true
        );

        // G2 — Cost analyzer. Apply a modest default, then let the consumer tune.
        graphqlBuilder.ModifyCostOptions(opts =>
        {
            opts.MaxFieldCost = 1000;
            opts.DefaultResolverCost = 10;
            config.CostOverride?.Invoke(opts);
        });

        // G3 — Conditional introspection. Default: on in Development, off elsewhere.
        // Consumer predicate wins. DisableIntrospection's delegate returns TRUE to
        // disable, so invert our "allow" predicate.
        graphqlBuilder.DisableIntrospection(
            (sp, _) =>
            {
                var httpCtx = sp.GetService<IHttpContextAccessor>()?.HttpContext;
                if (httpCtx is null)
                    return false;

                if (config.IntrospectionPredicate is not null)
                    return !config.IntrospectionPredicate(httpCtx);

                var env = sp.GetService<IHostEnvironment>();
                return env?.IsDevelopment() != true;
            }
        );

        // G5 — Subscription auth interceptors. Browsers cannot attach headers to
        // WebSocket upgrades, so each auth scheme registers an interceptor that
        // reads the credential from the connection_init payload. Wired only when
        // the corresponding principal resolver is present in DI.
        //
        // Cookie-based auth (Trax.Api.Auth.Oidc) needs no interceptor here: the
        // browser attaches cookies to the upgrade request and the cookie scheme
        // authenticates on the upgrade like any HTTP request.
        if (
            services.Any(sd =>
                sd.ServiceType == typeof(Trax.Api.Auth.ITraxPrincipalResolver<string>)
            )
        )
            graphqlBuilder.AddSocketSessionInterceptor<TraxApiKeySocketInterceptor>();

        if (
            services.Any(sd =>
                sd.ServiceType
                == typeof(Trax.Api.Auth.ITraxPrincipalResolver<Trax.Api.Auth.Jwt.JwtTokenInput>)
            )
        )
            graphqlBuilder.AddSocketSessionInterceptor<TraxJwtSocketInterceptor>();

        // G7 — HTTP execution authorization. Wired when the builder opted in via
        // RequireAuthorization(). The interceptor only runs for GraphQL execution
        // requests, so the BCP tool page and schema introspection stay reachable.
        if (config.AuthorizationRequired)
        {
            graphqlBuilder.AddHttpRequestInterceptor<TraxGraphQLAuthInterceptor>();
            services.AddHostedService<TraxGraphQLAuthPolicyValidator>();
        }

        // G6 — Per-request operation cap. Register as a document validator rule so
        // the rejection happens during validation, before any resolver runs.
        graphqlBuilder.ConfigureSchemaServices(sc =>
            sc.AddSingleton<IDocumentValidatorRule>(
                new OperationCountValidatorRule(config.MaxOperationsPerRequest)
            )
        );

        // G9 — Startup warning when model queries exist and the exposure surface is
        // not gated explicitly. Only logs once, at host start.
        if (config.ModelRegistrations.Count > 0)
            services.AddHostedService<GraphQLModelExposureWarningService>();
    }
}
