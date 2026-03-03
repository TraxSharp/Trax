using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Trax.Api.Exceptions;
using Trax.Mediator.Services.TrainAuthorization;
using Trax.Mediator.Services.TrainDiscovery;

namespace Trax.Api.Services.Authorization;

/// <summary>
/// Enforces <see cref="Trax.Effect.Attributes.TraxAuthorizeAttribute"/> requirements
/// against the current HTTP user using ASP.NET Core's authorization infrastructure.
/// </summary>
public class TrainAuthorizationService(
    IHttpContextAccessor httpContextAccessor,
    IAuthorizationService authorizationService
) : ITrainAuthorizationService
{
    public async Task AuthorizeAsync(TrainRegistration registration, CancellationToken ct = default)
    {
        if (registration.RequiredPolicies.Count == 0 && registration.RequiredRoles.Count == 0)
            return;

        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext?.User?.Identity?.IsAuthenticated != true)
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
            if (!registration.RequiredRoles.Any(r => user.IsInRole(r)))
                throw new TrainAuthorizationException(
                    registration.ServiceTypeName,
                    $"User lacks required role. Required one of: {string.Join(", ", registration.RequiredRoles)}"
                );
        }
    }
}
