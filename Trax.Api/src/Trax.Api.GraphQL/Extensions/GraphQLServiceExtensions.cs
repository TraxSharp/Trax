using System.Reflection;
using HotChocolate.AspNetCore;
using HotChocolate.Data;
using HotChocolate.Execution;
using HotChocolate.Execution.Configuration;
using HotChocolate.Types;
using HotChocolate.Validation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Trax.Api.Extensions;
using Trax.Api.GraphQL.Authorization;
using Trax.Api.GraphQL.Configuration;
using Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;
using Trax.Api.GraphQL.Errors;
using Trax.Api.GraphQL.Filtering.ListElements;
using Trax.Api.GraphQL.Hooks;
using Trax.Api.GraphQL.Introspection;
using Trax.Api.GraphQL.Mutations;
using Trax.Api.GraphQL.Projection;
using Trax.Api.GraphQL.Queries;
using Trax.Api.GraphQL.Sinks;
using Trax.Api.GraphQL.Startup;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Api.GraphQL.TypeModules;
using Trax.Api.GraphQL.Types;
using Trax.Api.GraphQL.Validation;
using Trax.Effect.Attributes;
using Trax.Effect.Configuration.TraxBuilder;
using Trax.Effect.Services.ChangeSignal;
using Trax.Effect.Services.TrainEventBroadcaster;
using Trax.Effect.Services.TrainLifecycleHookFactory;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.GraphQL.Extensions;

/// <summary>
/// Registers and maps the Trax GraphQL API. Call <c>AddTraxGraphQL</c> on the service collection
/// after <c>AddTrax(...)</c>, then <c>UseTraxGraphQL</c> on the application to map the endpoint.
/// </summary>
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
    /// <remarks>
    /// Requires <c>services.AddTrax(trax => ...)</c> to have been called first; otherwise this
    /// throws <see cref="InvalidOperationException"/>. The trains it exposes are the ones
    /// registered by <c>AddMediator(...)</c> inside <c>AddTrax</c>. Pair it with
    /// <see cref="UseTraxGraphQL(WebApplication, string, Action{IEndpointConventionBuilder}?)"/> on the
    /// built app, which maps the endpoint (at <c>/trax/graphql</c> by default). It also calls
    /// <see cref="Trax.Api.Extensions.ApiServiceExtensions.AddTraxApi"/>, so do not call that separately.
    /// </remarks>
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

        // Ensure the WebSocket upgrade middleware sits at the front of the
        // pipeline so subscriptions can always upgrade, no matter where the host
        // places UseTraxGraphQL() relative to other endpoint middleware (the
        // dashboard's Blazor endpoints, an explicit UseEndpoints, etc.). See
        // WebSocketsStartupFilter for the failure mode this prevents.
        services.TryAddEnumerable(
            ServiceDescriptor.Transient<IStartupFilter, WebSocketsStartupFilter>()
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

        // Fail fast (before any HotChocolate wiring) when an exposed train has not declared
        // its authorization posture. Runs against the same rule as the query-model side.
        ValidateTrainExposureAuthorization(trainRegistrations, config.AuthorizationRequired);

        services.AddTraxApi();
        services.AddSingleton<TrainTypeModule>();
        services.AddTransient<GraphQLSubscriptionHook>();
        services
            .AddSingleton<LifecycleHookFactory<GraphQLSubscriptionHook>>()
            .AddSingleton<ITrainLifecycleHookFactory>(sp =>
                sp.GetRequiredService<LifecycleHookFactory<GraphQLSubscriptionHook>>()
            );

        // Who may receive what from the subscriptions; see LifecycleSubscriptionAccess. It
        // evaluates policies with ASP.NET Core's IAuthorizationService, which needs logging.
        services.AddLogging();
        services.AddAuthorization();
        services.AddSingleton<LifecycleSubscriptionAccess>();

        // Deliver coalesced change signals to the local onDataChanged subscription. The change-
        // signal pipeline itself is registered by AddTrax(); this is the in-process delivery sink.
        services.AddSingleton<IChangeSignalSink, TopicEventSenderChangeSink>();

        // Exposing the operations (admin) surface means this host is an admin dashboard, which
        // should observe every train's lifecycle — not the per-train [TraxBroadcast] opt-in that
        // curates user-facing subscriptions. So the lifecycle hooks stream all trains here.
        var operationsExposed = config.OperationQueriesExposed || config.OperationMutationsExposed;
        services.AddSingleton(
            new TrainLifecycleStreamOptions { StreamAllTrains = operationsExposed }
        );

        // Fail fast at startup if the operations surface is exposed without its backing services,
        // instead of masking a runtime "Unexpected Execution Error" per request.
        if (operationsExposed)
        {
            var mutationsExposed = config.OperationMutationsExposed;
            services.AddHostedService(sp => new TraxOperationsServiceValidator(
                sp.GetRequiredService<IServiceProviderIsService>(),
                mutationsExposed
            ));
        }

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
                    var operations = d.Field("operations")
                        .Type<ObjectType<OperationsQueries>>()
                        .Resolve(_ => new OperationsQueries());
                    AuthorizeDirectives.Apply(operations, config.OperationsAuthorizeAttributes);
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
                    var operations = d.Field("operations")
                        .Type<ObjectType<OperationsMutations>>()
                        .Resolve(_ => new OperationsMutations());
                    AuthorizeDirectives.Apply(operations, config.OperationsAuthorizeAttributes);
                })
            );
        }

        // Wire HotChocolate's @authorize directive handler whenever an @authorize can reach the
        // schema. The directive runs against ASP.NET Core's IAuthorizationService, so RequireRole
        // and policy definitions registered via services.AddAuthorization(...) apply. Wiring is
        // conditional so the dependency stays opt-in for hosts with nothing gated: the interceptor
        // resolves IAuthenticationSchemeProvider, which a host that never called AddAuthentication()
        // does not have.
        //
        // Three things can put one in the schema: a [TraxAuthorize] query model, an operations
        // gate (GateOperations(...) or GateOperationsToAuthenticatedUsers()), and [TraxAuthorize] or [TraxAllowAnonymous] on a
        // type-extension resolver, which is what TypeExtensionExposureInterceptor requires of a
        // field that inherits no gate and turns into an @authorize directive.
        var authorizationInSchema =
            config.ModelRegistrations.Any(r => r.AuthorizeAttributes.Count > 0)
            || config.OperationsAuthorizeAttributes.Count > 0
            || AnyTypeExtensionDeclaresPosture(config.AdditionalTypeExtensions);

        if (authorizationInSchema)
        {
            // HotChocolate's authorization handler resolves ASP.NET Core's IAuthorizationService,
            // which a host that never called AddAuthorization() does not have: without this the
            // schema builds and then fails to activate the handler. Both calls are additive and
            // idempotent (they TryAdd), so a host that configured its own policies keeps them.
            services.AddLogging();
            services.AddAuthorization();

            graphqlBuilder.AddAuthorization();
        }

        ApplyHardeningDefaults(services, graphqlBuilder, config);

        // One HTTP interceptor establishes the caller for both @authorize and the endpoint policy.
        // HotChocolate keeps a single IHttpRequestInterceptor, so two would replace each other.
        // It resolves the ASP.NET Core services it needs from the request, and only its
        // configuration from the schema container.
        if (authorizationInSchema || config.AuthorizationRequired)
        {
            graphqlBuilder.BridgeApplicationService<GraphQLConfiguration>();
            graphqlBuilder.AddHttpRequestInterceptor<TraxHttpAuthenticationInterceptor>();
        }

        if (config.ModelRegistrations.Count > 0)
        {
            services.AddSingleton<QueryModelTypeModule>();
            graphqlBuilder.AddTypeModule<QueryModelTypeModule>();

            var hasGated = config.ModelRegistrations.Any(r => r.AuthorizeAttributes.Count > 0);
            var hasAnonymous = config.ModelRegistrations.Any(r => r.AllowAnonymous);

            if (hasGated)
                services.AddHostedService<QueryModelAuthorizationValidator>();

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
                // Scalar collection properties need their element filter input restricted:
                // `some/all/none: { neq: ... }` lowers to Any(x => x != value) over a
                // primitive collection, which no EF Core provider can translate, so it
                // passes validation and throws at execution. See ListElementFilterBinding.
                var listElementBindings = ListElementFilterBinding.Discover(
                    config
                        .ModelRegistrations.Where(r => r.Attribute.Filtering)
                        .Select(r => r.EntityType)
                );

                if (listElementBindings.Count > 0)
                {
                    // Whether a scalar collection is GIN-indexed decides which operator
                    // Npgsql emits for a membership filter, and nothing in the schema or
                    // the response reveals the answer. Say so at startup.
                    services.AddHostedService<QueryModelScalarCollectionIndexValidator>();
                }

                if (config.FilterModules.Count > 0 || listElementBindings.Count > 0)
                    graphqlBuilder.AddFiltering(convention =>
                    {
                        // Supplying a configure action replaces HotChocolate's default
                        // convention wiring, so re-establish the stock operations and
                        // queryable provider before layering the opt-in modules on top.
                        convention.AddDefaults();
                        ListElementFilterBinding.Apply(convention, listElementBindings);
                        foreach (var module in config.FilterModules)
                            module.Apply(convention);
                    });
                else
                    graphqlBuilder.AddFiltering();
            }

            if (config.ModelRegistrations.Any(r => r.Attribute.Sorting))
                graphqlBuilder.AddSorting();

            if (config.ModelRegistrations.Any(r => r.Attribute.Projection))
            {
                graphqlBuilder.AddProjections();
                // Declares the entity key as a projection requirement on hand-written
                // [ExtendObjectType] resolvers, which would otherwise receive a parent
                // whose key was never selected. See QueryModelProjection.
                graphqlBuilder.TryAddTypeInterceptor(
                    new QueryModelProjectionRequirementInterceptor(config)
                );
            }
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

        // The exposure census for type-extension fields. Registered whenever a type extension
        // could exist: through Trax's own AddTypeExtension(s), or through a ConfigureSchema
        // callback, which has full builder access and can add one Trax never sees. A host with
        // neither cannot have a type-extension field, and pays nothing.
        if (config.AdditionalTypeExtensions.Count > 0 || config.SchemaConfigurations.Count > 0)
        {
            var exposureReport = new TypeExtensionExposureReport();
            services.AddSingleton(exposureReport);
            graphqlBuilder.TryAddTypeInterceptor(
                new TypeExtensionExposureInterceptor(config, exposureReport)
            );
            services.AddHostedService<TypeExtensionExposureValidator>();
        }

        // Registered unconditionally, so remote lifecycle events and data-change signals reach
        // HotChocolate subscriptions whether UseBroadcaster() was called before AddTraxGraphQL or
        // after it. This used to be gated on ITrainEventReceiver already being in the collection,
        // which made the answer depend on where the caller happened to be in their own startup:
        // calling UseBroadcaster() afterwards dropped both handlers silently.
        //
        // Registering them with no broadcaster present costs nothing and cannot fail. The only
        // thing that resolves ITrainEventHandler is TrainEventReceiverService, which exists only
        // when a receiver does, so without one these are never constructed. Their sole required
        // dependency, ITopicEventSender, comes from the AddInMemorySubscriptions() call above and
        // is therefore always present when this line runs.
        services.AddTransient<ITrainEventHandler, GraphQLTrainEventHandler>();
        services.AddTransient<ITrainEventHandler, GraphQLDataChangeHandler>();

        return services;
    }

    /// <summary>
    /// Whether any registered type-extension class declares an authorization posture, on the class
    /// or on one of its resolvers. A declaration becomes a directive, so either flavour means the
    /// schema needs the authorization types registered.
    /// </summary>
    /// <remarks>
    /// Deciding this from the registration list rather than the built schema is deliberate: the
    /// answer is needed while the schema is still being configured, and a consumer who adds a
    /// declared type extension from a <c>ConfigureSchema</c> callback has the builder in hand and
    /// can call <c>AddAuthorization()</c> there.
    /// </remarks>
    private static bool AnyTypeExtensionDeclaresPosture(IReadOnlyList<Type> typeExtensions) =>
        typeExtensions.Any(type =>
            DeclaresPosture(type)
            || type.GetMembers(
                    BindingFlags.Public
                        | BindingFlags.NonPublic
                        | BindingFlags.Instance
                        | BindingFlags.Static
                        | BindingFlags.DeclaredOnly
                )
                .Any(DeclaresPosture)
        );

    private static bool DeclaresPosture(MemberInfo member) =>
        member.IsDefined(typeof(TraxAuthorizeAttribute), inherit: true)
        || member.IsDefined(typeof(TraxAllowAnonymousAttribute), inherit: true)
        // A foreign attribute is refused rather than honoured, but the refusal happens at schema
        // build, so the authorization types still have to be there for the schema to get that far.
        || TraxAuthorization.ForeignAuthorizationAttributes.Any(name =>
            member.GetCustomAttributes(inherit: true).Any(a => a.GetType().FullName == name)
        );

    /// <summary>
    /// Registers the Trax GraphQL schema on a named HotChocolate server ("trax").
    /// This avoids conflicts with a consumer's own default GraphQL schema.
    /// Only trains annotated with <c>[TraxQuery]</c> or <c>[TraxMutation]</c> get typed operations generated.
    /// </summary>
    /// <remarks>
    /// Requires <c>services.AddTrax(trax => ...)</c> to have been called first; otherwise this
    /// throws <see cref="InvalidOperationException"/>. The trains it exposes are the ones
    /// registered by <c>AddMediator(...)</c> inside <c>AddTrax</c>. Pair it with
    /// <see cref="UseTraxGraphQL(WebApplication, string, Action{IEndpointConventionBuilder}?)"/> on the
    /// built app, which maps the endpoint (at <c>/trax/graphql</c> by default). It also calls
    /// <see cref="Trax.Api.Extensions.ApiServiceExtensions.AddTraxApi"/>, so do not call that separately.
    /// </remarks>
    public static IServiceCollection AddTraxGraphQL(this IServiceCollection services) =>
        services.AddTraxGraphQL(builder => builder);

    /// <summary>
    /// Maps the Trax GraphQL endpoint at the specified route prefix.
    /// Uses a named schema so it coexists with other HotChocolate schemas
    /// in the same application. Use the optional <paramref name="configure"/> callback
    /// to apply endpoint conventions such as authorization or rate limiting.
    /// </summary>
    /// <remarks>
    /// The schema download (<c>?sdl</c>, <c>/schema</c>, <c>/schema.graphql</c>) and the GraphQL IDE
    /// on this endpoint follow the same per-request decision as introspection: allowed in
    /// Development, refused with 404 elsewhere, unless <c>AllowIntrospection(predicate)</c> says
    /// otherwise.
    /// A WebSocket upgrade to this endpoint that carries an <c>Origin</c> header is accepted only
    /// when the origin is on the endpoint's own host or is allowed, and is refused with
    /// <c>403</c> otherwise. The allowed origins are set with
    /// <c>TraxGraphQLBuilder.AllowSocketOrigins(...)</c>, and default to the host's CORS default
    /// policy. An upgrade with no <c>Origin</c> header is accepted.
    /// </remarks>
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
        // The WebSocket upgrade middleware is wired at the front of the pipeline
        // by WebSocketsStartupFilter (registered in AddTraxGraphQL), so it always
        // runs before endpoint execution regardless of host middleware ordering.
        var endpoint = app.MapGraphQL(routePrefix, SchemaName);
        // The schema download and the IDE follow the same per-request decision as introspection.
        SchemaDocumentGate.Apply(
            endpoint,
            new PathString(routePrefix.TrimEnd('/')),
            app.Services.GetRequiredService<IntrospectionPolicy>()
        );

        // A browser socket is accepted only from origins the host serves: the endpoint's own
        // host, or the allowed origins. The check wraps the endpoint's handler, so it runs
        // before HotChocolate accepts the upgrade. See
        // docs/adr/0007-a-browser-socket-is-accepted-only-from-origins-the-host-serves.md.
        var allowedOrigins = app.Services.GetService<GraphQLConfiguration>()?.SocketAllowedOrigins;
        endpoint.Add(endpointBuilder =>
        {
            var handler =
                endpointBuilder.RequestDelegate
                ?? throw new InvalidOperationException(
                    "The Trax GraphQL endpoint has no request handler to guard."
                );
            endpointBuilder.RequestDelegate = context =>
            {
                if (
                    context.WebSockets.IsWebSocketRequest
                    && !SocketOriginPolicy.IsAllowed(context, allowedOrigins)
                )
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                }

                return handler(context);
            };
        });

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

        // G3 — Introspection, decided per request. HotChocolate reads the DisableIntrospection
        // option once, when the executor is built and no request exists, so the schema-level
        // switch is always on and a request is let through only by the allowance middleware,
        // which runs before validation on every transport. See
        // docs/adr/0012-introspection-is-decided-per-request.md.
        services.TryAddSingleton(sp => new IntrospectionPolicy(
            config.IntrospectionPredicate,
            sp.GetService<IHostEnvironment>()
        ));
        graphqlBuilder.DisableIntrospection(true);
        // With no predicate the answer outside Development is always no, so the schema download
        // and the IDE are switched off in the schema's own server options too. That covers a host
        // that maps the endpoint itself instead of calling UseTraxGraphQL, whose per-request gate
        // it would otherwise skip.
        services
            .AddOptions<HotChocolate.AspNetCore.GraphQLServerOptions>(SchemaName)
            .Configure<IServiceProvider>(
                (options, sp) =>
                {
                    if (
                        config.IntrospectionPredicate is null
                        && sp.GetService<IHostEnvironment>()?.IsDevelopment() != true
                    )
                    {
                        options.EnableSchemaRequests = false;
                        options.Tool.Enable = false;
                    }
                }
            );
        graphqlBuilder.UseRequest(
            IntrospectionAllowanceMiddleware.Create,
            key: IntrospectionAllowanceMiddleware.Key,
            before: WellKnownRequestMiddleware.DocumentValidationMiddleware
        );

        // G8 — HTTP GET. Off unless the host opts in: a cross-site top-level navigation carries a
        // SameSite=Lax cookie, so a GET-executable query could be run as the signed-in user from
        // another site. An opted-in GET still needs the GraphQL-preflight header (a navigation
        // cannot add one) and runs queries only. Set on the schema, so it holds however the host
        // maps the endpoint. The IDE page and the SDL download are separate options.
        // See docs/adr/0024-graphql-get-is-off-unless-the-host-opts-in.md.
        graphqlBuilder.ModifyServerOptions(options =>
        {
            options.EnableGetRequests = config.GetRequestsAllowed;
            options.EnforceGetRequestsPreflightHeader = true;
            options.AllowedGetOperations = AllowedGetOperations.Query;
        });

        // G4 — Socket origins. A WebSocket upgrade is accepted only from origins the host
        // serves. The listener is on the Trax schema, so it sees every socket that schema serves
        // however the host mapped it, and no socket for another schema. UseTraxGraphQL() also
        // refuses before HotChocolate runs. See
        // docs/adr/0007-a-browser-socket-is-accepted-only-from-origins-the-host-serves.md.
        graphqlBuilder.AddDiagnosticEventListener<SocketOriginListener>();

        // G5 — Subscription auth. Browsers cannot attach headers to WebSocket upgrades, so the
        // API-key and JWT schemes read their credential from the connection_init payload.
        // HotChocolate runs one socket interceptor per schema, so one composite serves every
        // token scheme, and it is registered for every host: which schemes are active is read
        // from the completed container on the first connection, never from this collection, so
        // registration order cannot change it. See
        // docs/adr/0006-one-socket-interceptor-composes-every-token-scheme.md.
        //
        // Cookie-based auth (Trax.Api.Auth.Oidc) needs nothing here: the browser attaches
        // cookies to the upgrade request and the cookie scheme authenticates it like any HTTP
        // request. With no token scheme registered the composite accepts every connection.
        services.TryAddSingleton(sp => new TraxApplicationServices(sp));
        graphqlBuilder.BridgeApplicationService<TraxApplicationServices>();
        graphqlBuilder.AddSocketSessionInterceptor(sp => new TraxCompositeSocketInterceptor(
            sp.GetRequiredService<TraxApplicationServices>(),
            config.MaxOperationsPerConnection
        ));

        // A connection runs a bounded number of operations at once. The composite marks an
        // operation past the limit and this middleware refuses it with a coded error. See
        // docs/adr/0015-a-socket-runs-a-bounded-number-of-operations.md.
        graphqlBuilder.UseRequest(
            SocketOperationLimitRequestMiddleware.Create,
            key: SocketOperationLimitRequestMiddleware.Key,
            before: "DocumentCacheMiddleware"
        );

        // A backstop, not an ordering check: it fails the host if a token scheme is registered
        // and HotChocolate's accept-everything default is what would answer connection_init.
        services.AddHostedService(sp => new TraxSubscriptionAuthWiringValidator(
            sp.GetRequiredService<IServiceProviderIsService>(),
            sp.GetRequiredService<IRequestExecutorProvider>(),
            SchemaName
        ));

        // G7 — HTTP execution authorization. Wired when the builder opted in via
        // RequireAuthorization(). The interceptor only runs for GraphQL execution
        // requests, so the BCP tool page and schema introspection stay reachable.
        if (config.AuthorizationRequired)
        {
            services.AddAuthorization();
            services.AddHostedService<TraxGraphQLAuthPolicyValidator>();

            // The same policy for every operation on every transport. HotChocolate's request
            // pipeline is the one place an HTTP request and each operation a socket carries both
            // pass through. See docs/adr/0009-the-endpoint-policy-applies-to-every-transport.md.
            graphqlBuilder.UseRequest(
                EndpointPolicyRequestMiddleware.Create,
                key: EndpointPolicyRequestMiddleware.Key,
                before: "DocumentCacheMiddleware"
            );
        }

        // G6 — Per-request operation cap. Register as a document validator rule so
        // the rejection happens during validation, before any resolver runs.
        graphqlBuilder.ConfigureSchemaServices(sc =>
            sc.AddSingleton<IDocumentValidatorRule>(
                new OperationCountValidatorRule(config.MaxOperationsPerRequest)
            )
        );
    }

    /// <summary>
    /// Enforces the GraphQL exposure authorization rule for every train exposed via
    /// <c>[TraxQuery]</c>/<c>[TraxMutation]</c>: it must declare <c>[TraxAuthorize]</c> or
    /// <c>[TraxAllowAnonymous]</c> (never both), and <c>[TraxAllowAnonymous]</c> is contradictory
    /// when the endpoint is gated via <c>RequireAuthorization()</c>. Shares the decision with the
    /// query-model side via <see cref="ExposureAuthorizationRule"/>. Collects every offending train
    /// so a single host-startup failure lists them all rather than surfacing one at a time.
    /// </summary>
    private static void ValidateTrainExposureAuthorization(
        IReadOnlyList<TrainRegistration> registrations,
        bool endpointGated
    )
    {
        var violations = new List<string>();

        // A [TraxBroadcast] train streams its runs to subscribers, which exposes them as surely
        // as a query field does, so it answers the same question.
        foreach (
            var reg in registrations.Where(r => r.IsQuery || r.IsMutation || r.IsBroadcastEnabled)
        )
        {
            var violation = ExposureAuthorizationRule.Evaluate(
                hasAuthorize: reg.HasAuthorizeAttribute,
                hasAllowAnonymous: reg.HasAllowAnonymousAttribute,
                endpointGated: endpointGated
            );

            if (violation != ExposureViolation.None)
                violations.Add(
                    ExposureAuthorizationRule.BuildMessage(
                        reg.IsQuery || reg.IsMutation
                            ? "GraphQL-exposed train"
                            : "[TraxBroadcast] train",
                        reg.ServiceType.FullName!,
                        violation
                    )
                );
        }

        if (violations.Count == 0)
            return;

        throw new InvalidOperationException(
            "Trax GraphQL exposure authorization check failed:"
                + Environment.NewLine
                + string.Join(Environment.NewLine, violations.Select(v => "  - " + v))
        );
    }
}
