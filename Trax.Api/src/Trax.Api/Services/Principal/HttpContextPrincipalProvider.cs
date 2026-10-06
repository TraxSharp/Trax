using Microsoft.AspNetCore.Http;
using Trax.Mediator.Services.Principal;

namespace Trax.Api.Services.Principal;

/// <summary>
/// <see cref="ICurrentPrincipalProvider"/> implementation that reads the
/// <c>trax:principal-id</c> claim from the current request's
/// <see cref="HttpContext.User"/>. Replaces the mediator's default
/// <c>NullPrincipalProvider</c> when <c>AddTraxApi</c> is wired.
/// </summary>
internal sealed class HttpContextPrincipalProvider(IHttpContextAccessor httpContextAccessor)
    : ICurrentPrincipalProvider
{
    // Duplicated from Trax.Api.Auth.TraxAuthClaimTypes.PrincipalId to avoid
    // a cross-assembly dependency from Trax.Api → Trax.Api.Auth. Keep in sync.
    private const string PrincipalIdClaimType = "trax:principal-id";

    public string? GetCurrentPrincipalId()
    {
        var httpContext = httpContextAccessor.HttpContext;
        if (httpContext?.User.Identity?.IsAuthenticated != true)
            return null;

        return httpContext.User.FindFirst(PrincipalIdClaimType)?.Value;
    }
}
