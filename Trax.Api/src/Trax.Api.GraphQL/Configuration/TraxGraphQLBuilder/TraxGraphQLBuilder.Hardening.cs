using HotChocolate.CostAnalysis;
using Microsoft.AspNetCore.Http;
using Trax.Api.GraphQL.Subscriptions;

namespace Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;

/// <summary>
/// Fluent surface for GraphQL hardening: execution depth, cost analysis,
/// introspection gating, and per-request operation caps. Defaults are
/// applied during <c>AddTraxGraphQL</c> — the methods here override them.
/// </summary>
public partial class TraxGraphQLBuilder
{
    internal int MaxExecutionDepthValue { get; private set; } = 15;
    internal bool MaxExecutionDepthWasOverridden { get; private set; }

    internal Action<CostOptions>? CostOverride { get; private set; }

    internal Predicate<HttpContext>? IntrospectionPredicate { get; private set; }

    internal int MaxOperationsPerRequestValue { get; private set; } = 50;

    internal int MaxOperationsPerConnectionValue { get; private set; } = 100;

    internal TimeSpan MaxConnectionLifetimeValue { get; private set; } =
        TraxCompositeSocketInterceptor.DefaultMaxConnectionLifetime;

    internal TimeSpan ConnectionCredentialRecheckIntervalValue { get; private set; } =
        TraxCompositeSocketInterceptor.DefaultCredentialRecheckInterval;

    internal bool AuthorizationRequired { get; private set; }

    internal string? AuthorizationPolicy { get; private set; }

    internal bool GetRequestsAllowed { get; private set; }

    /// <summary>
    /// Sets the maximum GraphQL query depth. The default is <c>15</c>.
    /// Tighten this when running a public-facing schema where you want to
    /// reject deeply nested resource-exhaustion probes; loosen it (or keep
    /// the default) when consumers legitimately need deep model projections
    /// across several foreign-key hops or nested management namespaces like
    /// <c>operations.persistedOperations</c>.
    /// </summary>
    public TraxGraphQLBuilder MaxExecutionDepth(int depth)
    {
        if (depth <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(depth),
                depth,
                "MaxExecutionDepth must be positive."
            );
        MaxExecutionDepthValue = depth;
        MaxExecutionDepthWasOverridden = true;
        return this;
    }

    /// <summary>
    /// Customizes HotChocolate cost analysis options. Trax installs sensible
    /// defaults (a modest <see cref="CostOptions.MaxFieldCost"/>) and this
    /// callback fires after those defaults are applied so consumers can adjust.
    /// </summary>
    public TraxGraphQLBuilder ConfigureCost(Action<CostOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        CostOverride = configure;
        return this;
    }

    /// <summary>
    /// Supplies a predicate that decides, per request, whether the schema may be read. The
    /// default is "allow only in Development": schemas should not be enumerable by anonymous
    /// clients in production.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The predicate returns <c>true</c> to allow, <c>false</c> to deny, and once set it decides in
    /// every environment, Development included. It is called with the request's
    /// <see cref="HttpContext"/> for every operation on every transport (HTTP POST, GET,
    /// multipart and WebSocket), so it can read the authenticated user. On a socket that is the
    /// upgrade request, carrying the principal the socket authenticated as.
    /// </para>
    /// <para>
    /// An operation that selects introspection fields while denied fails validation with
    /// HotChocolate's <c>HC0046</c> error. The schema download (<c>?sdl</c>, <c>/schema</c>,
    /// <c>/schema.graphql</c>) and the GraphQL IDE follow the same answer on the endpoint
    /// <c>UseTraxGraphQL</c> maps, and a denied request gets 404. An operation built in-process,
    /// with no HTTP request, is answered by the environment alone.
    /// </para>
    /// </remarks>
    public TraxGraphQLBuilder AllowIntrospection(Predicate<HttpContext> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        IntrospectionPredicate = predicate;
        return this;
    }

    /// <summary>
    /// Caps the number of operations a single GraphQL request invokes. Default is <c>50</c>.
    /// An operation is a root field or a field under a namespace (<c>dispatch</c>,
    /// <c>discover</c>, <c>operations</c>, a nested namespace such as
    /// <c>operations { deadLetters }</c>, or a train's declared <c>Namespace</c>); the namespace
    /// field itself is not counted. Aliases and batched operations both count, selections inside
    /// fragment spreads or inline fragments count as if written in place, and selections sharing
    /// a response path count once. A request over the cap is refused during validation with
    /// <c>TRAX_TOO_MANY_OPERATIONS</c>, before any resolver runs.
    /// </summary>
    public TraxGraphQLBuilder MaxOperationsPerRequest(int maxOperations)
    {
        if (maxOperations <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maxOperations),
                maxOperations,
                "MaxOperationsPerRequest must be positive."
            );
        MaxOperationsPerRequestValue = maxOperations;
        return this;
    }

    /// <summary>
    /// Caps the number of operations one WebSocket connection runs at once. The default is
    /// <c>100</c>. An operation the connection starts past the cap gets a GraphQL error with code
    /// <c>TRAX_SOCKET_OPERATION_LIMIT</c> and takes no place; the connection stays open, and a
    /// place frees when one of its operations completes. The cap is per connection, so it does
    /// not limit how many connections a client opens.
    /// </summary>
    public TraxGraphQLBuilder MaxOperationsPerConnection(int maxOperations)
    {
        if (maxOperations <= 0)
            throw new ArgumentOutOfRangeException(
                nameof(maxOperations),
                maxOperations,
                "MaxOperationsPerConnection must be positive."
            );
        MaxOperationsPerConnectionValue = maxOperations;
        return this;
    }

    /// <summary>
    /// Sets how long one WebSocket connection stays open. The default is one hour, and the longest
    /// a host may set is one day. At the lifetime the connection is closed with code 1001 (Going
    /// Away), which graphql-ws clients reconnect after, authenticating again in the new
    /// <c>connection_init</c>. Every connection has one, however it authenticated: a JWT connection
    /// still closes earlier, at its token's <c>exp</c>, and a cookie connection at its sign-in's
    /// expiry. See
    /// <c>docs/adr/0033-a-socket-connection-has-a-maximum-lifetime-and-re-checks-its-key.md</c>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="lifetime"/> is not positive, or is longer than one day.
    /// </exception>
    public TraxGraphQLBuilder MaxConnectionLifetime(TimeSpan lifetime)
    {
        TraxCompositeSocketInterceptor.RequireConnectionDuration(lifetime, nameof(lifetime));
        MaxConnectionLifetimeValue = lifetime;
        return this;
    }

    /// <summary>
    /// Sets how often an API-key WebSocket connection's key is resolved again. The default is five
    /// minutes, and the longest a host may set is one day. When the key no longer resolves to the
    /// principal the connection opened as (it was revoked or rotated, or its roles or claims
    /// changed), or the resolver fails, the connection is closed with code 1008 (policy
    /// violation). Each re-check is one call to the host's API-key resolver per open connection.
    /// See <c>docs/adr/0033-a-socket-connection-has-a-maximum-lifetime-and-re-checks-its-key.md</c>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="interval"/> is not positive, or is longer than one day.
    /// </exception>
    public TraxGraphQLBuilder RecheckConnectionCredentialsEvery(TimeSpan interval)
    {
        TraxCompositeSocketInterceptor.RequireConnectionDuration(interval, nameof(interval));
        ConnectionCredentialRecheckIntervalValue = interval;
        return this;
    }

    /// <summary>
    /// Gates every GraphQL operation the Trax schema executes behind an authorization policy, on
    /// every transport: an HTTP request and each operation a subscription socket carries pass the
    /// same check, introspection operations included. A principal that is not authenticated never
    /// satisfies it, and a socket is also checked once at <c>connection_init</c>.
    /// <para>
    /// The GraphQL IDE page (HTML GET) and the schema download are not operations and are not
    /// gated by this policy; they follow <c>AllowIntrospection</c>.
    /// </para>
    /// <para>
    /// Pass no argument to use the combined Trax auth policy
    /// (<c>TraxAuthClaimTypes.TraxAuthPolicy</c>), which every <c>AddTrax*Auth</c>
    /// extension registers its scheme into. Pass an explicit policy name to
    /// require something more specific (for example <c>ApiKeyDefaults.PolicyName</c>
    /// to require an API key even if other schemes are registered).
    /// </para>
    /// <para>
    /// Failed checks surface as a GraphQL error with code <c>TRAX_AUTHORIZATION</c>
    /// rather than an HTTP 401, so the IDE renders them in its result pane and
    /// the response shape stays consistent with per-train authorization failures.
    /// This is the endpoint gate the startup exposure checks honour; an ASP.NET endpoint
    /// convention added when mapping the route does not count, because those checks run before
    /// the route is mapped. See <c>docs/adr/0009-the-endpoint-policy-applies-to-every-transport.md</c>.
    /// </para>
    /// </summary>
    public TraxGraphQLBuilder RequireAuthorization(string? policy = null)
    {
        AuthorizationRequired = true;
        AuthorizationPolicy = policy;
        return this;
    }

    /// <summary>
    /// Serves GraphQL queries over HTTP GET. GET is off by default: the endpoint executes only
    /// POSTed operations (and subscriptions over the socket).
    /// </summary>
    /// <remarks>
    /// A browser attaches a <c>SameSite=Lax</c> cookie to a cross-site top-level navigation, so
    /// with GET on, a link on another site could run a <c>[TraxQuery]</c> train as the signed-in
    /// user. The response is not readable cross-site, but the train still runs. Opting in keeps
    /// two guards: a GET must carry the <c>GraphQL-preflight</c> header, which a navigation cannot
    /// add, and only queries run over GET (a mutation is refused). Opt in only for a client that
    /// needs GET, such as a CDN caching persisted queries by id, and send the header from it.
    /// <para>
    /// The setting belongs to the <c>trax</c> schema, so it holds whether the endpoint is mapped
    /// with <c>UseTraxGraphQL</c> or directly with <c>MapGraphQL(path, "trax")</c>. The IDE page
    /// and the SDL download are separate HotChocolate options and are not affected.
    /// See <c>docs/adr/0024-graphql-get-is-off-unless-the-host-opts-in.md</c>.
    /// </para>
    /// </remarks>
    public TraxGraphQLBuilder AllowGetRequests()
    {
        GetRequestsAllowed = true;
        return this;
    }
}
