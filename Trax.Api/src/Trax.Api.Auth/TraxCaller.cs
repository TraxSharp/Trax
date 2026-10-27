using Microsoft.AspNetCore.Http;
using Trax.Mediator.Services.TrustedExecution;

namespace Trax.Api.Auth;

/// <summary>
/// Always-present caller context. Unlike <see cref="TraxPrincipal"/>, which throws
/// when no authenticated user is on the current scope, <see cref="TraxCaller"/> is
/// safe to inject anywhere and reports the three independent flags downstream code
/// needs to decide whether to do anything:
///
/// <list type="bullet">
/// <item><see cref="IsAuthenticated"/> — an authenticated Trax principal exists on the current HTTP request.</item>
/// <item><see cref="IsTrusted"/> — execution is inside an <see cref="ITrustedExecutionScope.BeginTrusted"/> block. Trax opens one only around the authorization of a remote run's named train and of a system-owned state machine's invoked train, so a train's own code sees <c>false</c>.</item>
/// <item><see cref="Principal"/> — the resolved <see cref="TraxPrincipal"/>, or <c>null</c> when <see cref="IsAuthenticated"/> is false.</item>
/// </list>
///
/// <para>
/// Designed for code that runs in both authenticated and anonymous flows: row-level
/// filters on <c>[TraxAllowAnonymous]</c> entities, junctions reused across gated
/// and ungated trains, custom authorization hooks. Inject <see cref="TraxPrincipal"/>
/// instead when the caller is required to be authenticated and you want fail-loud
/// behavior on misuse.
/// </para>
/// </summary>
/// <remarks>
/// <para>
/// <strong>Security: <see cref="IsTrusted"/> is true only where a host opens a trusted scope.</strong>
/// The flag reflects <see cref="ITrustedExecutionScope.IsTrusted"/>, an in-process
/// <c>AsyncLocal</c> opened only by C# code calling
/// <see cref="ITrustedExecutionScope.BeginTrusted"/>. No HTTP header, cookie, query
/// parameter, body field, JWT claim, or GraphQL input maps to it. A scheduler runner's
/// endpoints (<c>UseTraxRunEndpoint</c>, the Lambda runner's run path) open one only while
/// they authorize the train a request names, because the scheduler already authorized
/// it; the train then runs outside it, so its junctions, and any train it queues or runs,
/// see <c>false</c> and are checked as they would be on the scheduler's own host. Whoever
/// the runner's authorization posture admits (a request signed with its key, a caller its
/// policy accepts, or anyone when the host allowed unsigned requests) can still run any
/// train the runner hosts, so give the runner a posture that admits only the scheduler.
/// </para>
/// <para>
/// In-process developer mistakes (a junction or resolver that opens a trusted scope
/// inside an HTTP request) can elevate <see cref="IsTrusted"/> for that request.
/// Trax does not enforce this with a runtime wall — the contract on
/// <see cref="ITrustedExecutionScope.BeginTrusted"/> is documented in its XML
/// summary. A Roslyn analyzer that restricts <c>BeginTrusted</c> call sites to an
/// allowlist of framework assemblies is the recommended hardening for hosts that
/// need a compile-time guarantee.
/// </para>
/// <para>
/// NO WARRANTY. See SECURITY-DISCLAIMER.md.
/// </para>
/// </remarks>
/// <seealso href="https://traxsharp.net/docs/sdk-reference/api-auth/trax-caller">SDK reference</seealso>
public sealed class TraxCaller
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly ITrustedExecutionScope _trustedScope;

    /// <summary>
    /// Constructs a <see cref="TraxCaller"/> bound to the current HTTP context
    /// accessor and the in-process trusted-execution scope. Resolved as a scoped
    /// service by <see cref="TraxAuthServiceCollectionExtensions.AddTraxPrincipalAccessor"/>.
    /// </summary>
    public TraxCaller(IHttpContextAccessor httpContextAccessor, ITrustedExecutionScope trustedScope)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        ArgumentNullException.ThrowIfNull(trustedScope);
        _httpContextAccessor = httpContextAccessor;
        _trustedScope = trustedScope;
    }

    /// <summary>
    /// True when an authenticated Trax principal is present on the current scope.
    /// Equivalent to <c><see cref="Principal"/> is not null</c>.
    /// </summary>
    public bool IsAuthenticated => Principal is not null;

    /// <summary>
    /// True when execution is inside an active
    /// <see cref="ITrustedExecutionScope.BeginTrusted"/> block. Reflects in-process
    /// <c>AsyncLocal</c> state opened by framework infrastructure: a runner while it
    /// authorizes the train a request names, not while the train runs.
    /// <para>
    /// Not settable from any HTTP surface. See the class-level remarks.
    /// </para>
    /// </summary>
    public bool IsTrusted => _trustedScope.IsTrusted;

    /// <summary>
    /// The current authenticated principal, or <c>null</c> when the current scope
    /// is anonymous (no HTTP context, no authenticated user, or an authenticated
    /// user whose claims do not include a Trax principal id).
    /// <para>
    /// Property is read on each access so an authentication that completes after
    /// <see cref="TraxCaller"/> was constructed (e.g., via Trax's
    /// <c>QueryModelAuthenticationInterceptor</c>) is immediately visible.
    /// </para>
    /// </summary>
    public TraxPrincipal? Principal
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user is null)
                return null;
            return user.TryGetTraxPrincipal(out var principal) ? principal : null;
        }
    }
}
