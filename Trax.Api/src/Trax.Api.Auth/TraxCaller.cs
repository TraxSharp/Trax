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
/// <item><see cref="IsTrusted"/> — execution is inside an <see cref="ITrustedExecutionScope.BeginTrusted"/> block, opened by framework infrastructure (scheduler dispatch, remote worker job runner).</item>
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
/// <strong>Security: <see cref="IsTrusted"/> cannot be set from HTTP.</strong>
/// The flag reflects <see cref="ITrustedExecutionScope.IsTrusted"/>, an in-process
/// <c>AsyncLocal</c> opened only by C# code calling
/// <see cref="ITrustedExecutionScope.BeginTrusted"/>. No HTTP header, cookie, query
/// parameter, body field, JWT claim, or GraphQL input maps to it. An external caller
/// with HTTP-only access cannot make this flag true for their request.
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
    /// <c>AsyncLocal</c> state opened by framework infrastructure
    /// (<c>TraxRequestHandler</c> in the scheduler, remote-worker runners).
    /// <para>
    /// Cannot be set from HTTP. See the class-level remarks for the threat model.
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
