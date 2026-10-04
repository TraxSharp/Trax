using HotChocolate.CostAnalysis;
using HotChocolate.Execution.Configuration;
using Microsoft.AspNetCore.Http;
using Trax.Api.GraphQL.Filtering;
using Trax.Api.GraphQL.Subscriptions;
using Trax.Effect.Attributes;

namespace Trax.Api.GraphQL.Configuration;

/// <summary>
/// Holds the resolved configuration for the Trax GraphQL schema,
/// including discovered query model registrations.
/// </summary>
public class GraphQLConfiguration
{
    /// <summary>
    /// Every entity discovered through <c>AddDbContext&lt;T&gt;()</c> that carries
    /// <c>[TraxQueryModel]</c>, one registration per entity. A consumer's own type module can read
    /// it to extend the generated query types.
    /// </summary>
    public IReadOnlyList<QueryModelRegistration> ModelRegistrations { get; }

    /// <summary>
    /// Additional HotChocolate <see cref="HotChocolate.Execution.Configuration.TypeModule"/> types
    /// registered by consumers via <c>AddTypeModule&lt;T&gt;()</c>.
    /// </summary>
    internal IReadOnlyList<Type> AdditionalTypeModules { get; }

    /// <summary>
    /// Additional HotChocolate type extension classes (e.g. <c>[ExtendObjectType]</c>)
    /// registered by consumers via <c>AddTypeExtension&lt;T&gt;()</c> or
    /// <c>AddTypeExtensions(assembly)</c>.
    /// </summary>
    internal IReadOnlyList<Type> AdditionalTypeExtensions { get; }

    /// <summary>
    /// Callbacks to apply arbitrary <see cref="IRequestExecutorBuilder"/> configuration
    /// registered by consumers via <c>ConfigureSchema()</c>.
    /// </summary>
    internal IReadOnlyList<Action<IRequestExecutorBuilder>> SchemaConfigurations { get; }

    /// <summary>
    /// Max GraphQL execution depth (default 15). Queries deeper than this are rejected
    /// during validation.
    /// </summary>
    public int MaxExecutionDepth { get; }

    /// <summary>
    /// Optional cost-analysis override. Applied after Trax defaults.
    /// </summary>
    internal Action<CostOptions>? CostOverride { get; }

    /// <summary>
    /// Predicate that gates introspection per request. Null → default
    /// (Development-only). Returns <c>true</c> to allow, <c>false</c> to deny.
    /// </summary>
    internal Predicate<HttpContext>? IntrospectionPredicate { get; }

    /// <summary>
    /// Maximum top-level GraphQL selections per request (default 50).
    /// </summary>
    public int MaxOperationsPerRequest { get; }

    /// <summary>
    /// Maximum operations one WebSocket connection runs at once (default 100).
    /// </summary>
    public int MaxOperationsPerConnection { get; internal init; } = 100;

    /// <summary>
    /// How long one WebSocket connection stays open before it is closed with Going Away (default
    /// one hour).
    /// </summary>
    public TimeSpan MaxConnectionLifetime { get; internal init; } =
        TraxCompositeSocketInterceptor.DefaultMaxConnectionLifetime;

    /// <summary>
    /// How often an API-key WebSocket connection's key is resolved again (default five minutes).
    /// </summary>
    public TimeSpan ConnectionCredentialRecheckInterval { get; internal init; } =
        TraxCompositeSocketInterceptor.DefaultCredentialRecheckInterval;

    /// <summary>
    /// True when <c>RequireAuthorization()</c> was called on the builder.
    /// Gates GraphQL execution (HTTP POST and GET-with-query); the schema download, the
    /// GraphQL IDE and introspection follow <see cref="IntrospectionPredicate"/> instead.
    /// </summary>
    internal bool AuthorizationRequired { get; }

    /// <summary>
    /// Authorization policy applied by the execution interceptor when
    /// <see cref="AuthorizationRequired"/> is true. <c>null</c> means
    /// "use the combined Trax auth policy" — every <c>AddTrax*Auth</c>
    /// registers its scheme into that policy.
    /// </summary>
    internal string? AuthorizationPolicy { get; }

    /// <summary>
    /// True when the consumer opted in via
    /// <c>TraxGraphQLBuilder.ExposeOperationQueries()</c>. When false the
    /// <c>operations</c> field is omitted from <c>RootQuery</c>.
    /// </summary>
    public bool OperationQueriesExposed { get; }

    /// <summary>
    /// True when the consumer opted in via
    /// <c>TraxGraphQLBuilder.ExposeOperationMutations()</c>. When false the
    /// <c>operations</c> field is omitted from <c>RootMutation</c>.
    /// </summary>
    public bool OperationMutationsExposed { get; }

    /// <summary>
    /// Every <c>[TraxAuthorize]</c> shape passed to
    /// <c>TraxGraphQLBuilder.GateOperations(...)</c> or
    /// <c>GateOperationsToAuthenticatedUsers()</c>. Empty when the namespace carries no gate of
    /// its own, in which case the endpoint gate (or
    /// <c>AllowAnonymousOperations()</c>) is what governs it.
    /// </summary>
    internal IReadOnlyList<TraxAuthorizeAttribute> OperationsAuthorizeAttributes { get; }

    /// <summary>
    /// Opt-in filter convention modules registered via
    /// <c>TraxGraphQLBuilder.ConfigureFiltering()</c>. Empty by default, in which case
    /// HotChocolate's stock filtering convention is used unchanged. When non-empty, each
    /// module is applied inside the <c>AddFiltering(convention =&gt; ...)</c> callback.
    /// </summary>
    internal IReadOnlyList<ITraxFilterModule> FilterModules { get; }

    /// <summary>
    /// Whether GraphQL queries are served over HTTP GET, set by
    /// <c>TraxGraphQLBuilder.AllowGetRequests()</c>. Off by default. See
    /// <c>docs/adr/0024-graphql-get-is-off-unless-the-host-opts-in.md</c>.
    /// </summary>
    internal bool GetRequestsAllowed { get; init; }

    /// <summary>
    /// Whether broadcast subscribers see the answers on <c>onJunctionEvent</c>, set by
    /// <c>TraxGraphQLBuilder.AllowJunctionAnswersForBroadcastSubscribers()</c>. Off by default.
    /// </summary>
    internal bool JunctionAnswersForBroadcastAllowed { get; init; }

    /// <summary>
    /// The classes the query models reach through their properties that are not query models
    /// themselves, with the posture each declares. See <see cref="NavigationTargetPosture"/>.
    /// </summary>
    internal IReadOnlyList<NavigationTargetPosture> NavigationTargets { get; init; } = [];

    /// <summary>
    /// Origins set through <c>TraxGraphQLBuilder.AllowSocketOrigins()</c>, normalized, or
    /// <c>null</c> when the host's CORS default policy decides which origins may open a socket.
    /// </summary>
    internal IReadOnlyList<string>? SocketAllowedOrigins { get; init; }

    /// <summary>
    /// Creates the configuration. <see cref="Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder.TraxGraphQLBuilder"/>
    /// builds it from the builder calls; hosts receive it from DI rather than constructing it.
    /// </summary>
    /// <param name="modelRegistrations">The discovered <c>[TraxQueryModel]</c> entities.</param>
    /// <param name="additionalTypeModules">Type module types added with <c>AddTypeModule&lt;T&gt;()</c>.</param>
    /// <param name="schemaConfigurations">Callbacks added with <c>ConfigureSchema()</c>, applied to the request executor builder.</param>
    /// <param name="additionalTypeExtensions">Type extension types added with <c>AddTypeExtension&lt;T&gt;()</c> or <c>AddTypeExtensions(assembly)</c>.</param>
    /// <param name="maxExecutionDepth">The deepest query accepted; deeper queries fail validation.</param>
    /// <param name="costOverride">An optional cost-analysis override, applied after the Trax defaults.</param>
    /// <param name="introspectionPredicate">Decides per request whether introspection is allowed; <c>null</c> allows it in Development only.</param>
    /// <param name="maxOperationsPerRequest">The most top-level selections one request may carry.</param>
    /// <param name="authorizationRequired">Whether <c>RequireAuthorization()</c> was called, so every execution needs an authorized caller.</param>
    /// <param name="authorizationPolicy">The policy that gate applies, or <c>null</c> for the combined Trax auth policy.</param>
    /// <param name="operationQueriesExposed">Whether the <c>operations</c> query namespace is in the schema.</param>
    /// <param name="operationMutationsExposed">Whether the <c>operations</c> mutation namespace is in the schema.</param>
    /// <param name="filterModules">Filter convention modules added with <c>ConfigureFiltering()</c>; <c>null</c> for none.</param>
    /// <param name="operationsAuthorizeAttributes">The authorization shapes that gate the <c>operations</c> namespace; <c>null</c> for no namespace gate of its own.</param>
    internal GraphQLConfiguration(
        IReadOnlyList<QueryModelRegistration> modelRegistrations,
        IReadOnlyList<Type> additionalTypeModules,
        IReadOnlyList<Action<IRequestExecutorBuilder>> schemaConfigurations,
        IReadOnlyList<Type> additionalTypeExtensions,
        int maxExecutionDepth = 15,
        Action<CostOptions>? costOverride = null,
        Predicate<HttpContext>? introspectionPredicate = null,
        int maxOperationsPerRequest = 50,
        bool authorizationRequired = false,
        string? authorizationPolicy = null,
        bool operationQueriesExposed = false,
        bool operationMutationsExposed = false,
        IReadOnlyList<ITraxFilterModule>? filterModules = null,
        IReadOnlyList<TraxAuthorizeAttribute>? operationsAuthorizeAttributes = null
    )
    {
        ModelRegistrations = modelRegistrations;
        AdditionalTypeModules = additionalTypeModules;
        SchemaConfigurations = schemaConfigurations;
        AdditionalTypeExtensions = additionalTypeExtensions;
        MaxExecutionDepth = maxExecutionDepth;
        CostOverride = costOverride;
        IntrospectionPredicate = introspectionPredicate;
        MaxOperationsPerRequest = maxOperationsPerRequest;
        AuthorizationRequired = authorizationRequired;
        AuthorizationPolicy = authorizationPolicy;
        OperationQueriesExposed = operationQueriesExposed;
        OperationMutationsExposed = operationMutationsExposed;
        FilterModules = filterModules ?? [];
        OperationsAuthorizeAttributes = operationsAuthorizeAttributes ?? [];
    }
}
