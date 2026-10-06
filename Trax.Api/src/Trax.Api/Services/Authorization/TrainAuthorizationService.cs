using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Trax.Api.Exceptions;
using Trax.Mediator.Services.TrainAuthorization;
using Trax.Mediator.Services.TrainDiscovery;
using Trax.Mediator.Services.TrustedExecution;

namespace Trax.Api.Services.Authorization;

/// <summary>
/// Enforces <see cref="Trax.Effect.Attributes.TraxAuthorizeAttribute"/> requirements
/// against the current HTTP user using ASP.NET Core's authorization infrastructure.
/// </summary>
/// <remarks>
/// Trust model. Authorization is enforced at API submission time (GraphQL resolver,
/// REST endpoint), where an <see cref="HttpContext"/> is present and an authenticated
/// user is expected. Scheduler pipelines, remote-worker job runners, and other
/// infrastructure paths pull pre-authorized work from the queue and mark themselves as
/// trusted via <see cref="ITrustedExecutionScope.BeginTrusted"/>. When a trusted scope
/// is active, the check is skipped and logged at Information level.
/// <para>
/// This service is fail-closed. Without a trusted scope and without an authenticated
/// user in an <see cref="HttpContext"/>, authorized trains are rejected.
/// </para>
/// <para>
/// Combinator semantics for <see cref="Trax.Effect.Attributes.TraxAuthorizeAttribute"/> are
/// ASP.NET Core's for multiple <c>[Authorize]</c> attributes: every policy must pass, and every
/// attribute that names roles is a requirement of its own, met by holding at least one of the
/// roles it lists (see <see cref="TrainRoleRequirements"/>). A bare <c>[TraxAuthorize]</c>
/// requires an authenticated user but imposes no policy or role requirement.
/// </para>
/// </remarks>
public class TrainAuthorizationService(
    IHttpContextAccessor httpContextAccessor,
    IAuthorizationService authorizationService,
    ITrustedExecutionScope trustedScope,
    ILogger<TrainAuthorizationService> logger
) : ITrainAuthorizationService
{
    /// <inheritdoc/>
    public async Task AuthorizeAsync(TrainRegistration registration, CancellationToken ct = default)
    {
        if (!registration.HasAuthorizeAttribute)
            return;

        if (trustedScope.IsTrusted)
        {
            logger.LogInformation(
                "Skipping authorization for train {TrainName} under trusted scope '{Reason}'.",
                registration.ServiceTypeName,
                trustedScope.CurrentReason
            );
            return;
        }

        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext is null)
            throw new TrainAuthorizationException(
                registration.ServiceTypeName,
                "No request context and no trusted execution scope."
            );

        if (httpContext.User?.Identity?.IsAuthenticated != true)
            throw new TrainAuthorizationException(
                registration.ServiceTypeName,
                "No authenticated user."
            );

        var user = httpContext.User;

        foreach (var policy in registration.RequiredPolicies)
        {
            var result = await authorizationService.AuthorizeAsync(user, policy);
            if (!result.Succeeded)
                throw new TrainAuthorizationException(
                    registration.ServiceTypeName,
                    $"Policy '{policy}' not satisfied."
                );
        }

        // Exact, case-sensitive comparison, as ASP.NET Core's RequireRole and @authorize make
        // it: IsInRole matches each identity's role claim type ordinally. See Trax.Docs
        // adr/0026-train-roles-match-exactly-like-authorize.md.
        foreach (var roles in TrainRoleRequirements.For(registration))
        {
            if (!roles.Any(user.IsInRole))
                throw new TrainAuthorizationException(
                    registration.ServiceTypeName,
                    $"User lacks required role. Required one of: {string.Join(", ", roles)}"
                );
        }
    }
}
