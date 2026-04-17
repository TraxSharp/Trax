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

        var graphqlBuilder = services
            .AddGraphQLServer(SchemaName)
            .AddQueryType<RootQuery>()
            .AddMutationType<RootMutation>()
            .AddSubscriptionType<LifecycleSubscriptions>()
            .AddType<ObjectType<OperationsMutations>>()
            .AddType<ObjectType<OperationsQueries>>()
            .AddType<TrainLifecycleEventType>()
            .AddTypeModule<TrainTypeModule>()
            .AddErrorFilter<TraxErrorFilter>()
            .AddInMemorySubscriptions();

        ApplyHardeningDefaults(services, graphqlBuilder, config);

        if (config.ModelRegistrations.Count > 0)
        {
            services.AddSingleton<QueryModelTypeModule>();
            graphqlBuilder.AddTypeModule<QueryModelTypeModule>();

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
                graphqlBuilder.AddFiltering();

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
        // G1 — Max execution depth. Defaults to 4 unless the consumer overrides.
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
