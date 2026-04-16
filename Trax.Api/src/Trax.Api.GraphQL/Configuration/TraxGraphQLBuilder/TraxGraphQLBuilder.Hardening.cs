using HotChocolate.CostAnalysis;
using Microsoft.AspNetCore.Http;

namespace Trax.Api.GraphQL.Configuration.TraxGraphQLBuilder;

/// <summary>
/// Fluent surface for GraphQL hardening: execution depth, cost analysis,
/// introspection gating, and per-request operation caps. Defaults are
/// applied during <c>AddTraxGraphQL</c> — the methods here override them.
/// </summary>
public partial class TraxGraphQLBuilder
{
    internal int MaxExecutionDepthValue { get; private set; } = 4;
    internal bool MaxExecutionDepthWasOverridden { get; private set; }

    internal Action<CostOptions>? CostOverride { get; private set; }

    internal Predicate<HttpContext>? IntrospectionPredicate { get; private set; }

    internal int MaxOperationsPerRequestValue { get; private set; } = 50;

    internal bool AuthorizationRequired { get; private set; }

    internal string? AuthorizationPolicy { get; private set; }

    /// <summary>
    /// Sets the maximum GraphQL query depth. The default is <c>4</c>.
    /// Callers that legitimately need deeper queries (e.g. model projections
    /// with several foreign-key hops) should raise this deliberately; the
    /// default is deep enough for train-shaped operations but rejects
    /// resource-exhaustion probes.
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
    /// Supplies a predicate that decides, per request, whether introspection
    /// is allowed. The default is "allow only in Development" — schemas should
    /// not be enumerable by anonymous clients in production.
    /// </summary>
    /// <remarks>
    /// Predicate returns <c>true</c> to allow introspection, <c>false</c> to deny.
    /// Introspection queries that hit a denying predicate fail validation with
    /// the usual HotChocolate error.
    /// </remarks>
    public TraxGraphQLBuilder AllowIntrospection(Predicate<HttpContext> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        IntrospectionPredicate = predicate;
        return this;
    }

    /// <summary>
    /// Caps the number of top-level selections in a single GraphQL request
    /// (aliased fields + batched operations both count). Default is <c>50</c>.
    /// Rejects amplification attacks that submit hundreds of aliased train
    /// invocations in a single HTTP request.
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
    /// Gates GraphQL execution behind an authorization policy. The Banana Cake
    /// Pop tool page (HTML GET) and schema introspection are governed
    /// independently and remain reachable; only requests that carry an actual
    /// GraphQL operation are checked.
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
    /// Subscription auth is governed by the WebSocket interceptor wired by
    /// <c>AddTraxApiKeyAuth</c>; this method only affects HTTP execution.
    /// </para>
    /// </summary>
    public TraxGraphQLBuilder RequireAuthorization(string? policy = null)
    {
        AuthorizationRequired = true;
        AuthorizationPolicy = policy;
        return this;
    }
}
