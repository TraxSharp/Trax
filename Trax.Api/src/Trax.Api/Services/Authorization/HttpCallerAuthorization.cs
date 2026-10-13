using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Trax.Effect.StateMachine.Persistence;

namespace Trax.Api.Services.Authorization;

/// <summary>
/// Evaluates a state-machine event's <c>Requires</c> and <c>RequiresPolicy</c> rules against the current HTTP user,
/// the way <see cref="TrainAuthorizationService"/> evaluates <c>[TraxAuthorize]</c>: a role matches exactly and
/// case-sensitively (<c>Trax.Docs/adr/0026-train-roles-match-exactly-like-authorize.md</c>), and a policy through
/// ASP.NET Core's <see cref="IAuthorizationService"/>. A request with no authenticated user holds no role and
/// satisfies no policy. Registered by <c>AddTraxApi</c>.
/// </summary>
internal sealed class HttpCallerAuthorization(
    IHttpContextAccessor httpContextAccessor,
    IAuthorizationService authorizationService,
    IAuthorizationPolicyProvider policyProvider
) : ICallerAuthorization
{
    /// <inheritdoc/>
    public bool IsInRole(string role) => AuthenticatedUser() is { } user && user.IsInRole(role);

    /// <inheritdoc/>
    public async Task<bool> SatisfiesPolicy(
        string policy,
        CancellationToken cancellationToken = default
    ) =>
        AuthenticatedUser() is { } user
        && (await authorizationService.AuthorizeAsync(user, policy)).Succeeded;

    /// <inheritdoc/>
    public async Task<bool> IsPolicyRegistered(string policy) =>
        await policyProvider.GetPolicyAsync(policy) is not null;

    private System.Security.Claims.ClaimsPrincipal? AuthenticatedUser() =>
        httpContextAccessor.HttpContext?.User is { Identity.IsAuthenticated: true } user
            ? user
            : null;
}
