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
/// Combinator semantics for <see cref="Trax.Effect.Attributes.TraxAuthorizeAttribute"/>:
/// policies AND (every policy must pass), roles OR (the user must hold at least one).
/// A bare <c>[TraxAuthorize]</c> requires an authenticated user but imposes no policy
/// or role requirement.
/// </para>
/// </remarks>
public class TrainAuthorizationService(
    IHttpContextAccessor httpContextAccessor,
    IAuthorizationService authorizationService,
    ITrustedExecutionScope trustedScope,
    ILogger<TrainAuthorizationService> logger
) : ITrainAuthorizationService
{
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

        if (registration.RequiredRoles.Count > 0)
        {
            // Discovery normalizes RequiredRoles to upper-invariant. Do the same here
            // defensively on both sides so hosts that build registrations by hand (tests,
            // custom discovery) still get case-insensitive matching. user.IsInRole() uses
            // ordinal equality on the raw claim value and would silently deny when a train
            // declares [TraxAuthorize(Roles="admin")] but the principal carries
            // ClaimTypes.Role = "Admin" (or vice-versa).
            var userRoles = user.FindAll(ClaimTypes.Role)
                .Select(c => c.Value.ToUpperInvariant())
                .ToHashSet();

            if (!registration.RequiredRoles.Any(r => userRoles.Contains(r.ToUpperInvariant())))
                throw new TrainAuthorizationException(
                    registration.ServiceTypeName,
                    $"User lacks required role. Required one of: {string.Join(", ", registration.RequiredRoles)}"
                );
        }
    }
}
