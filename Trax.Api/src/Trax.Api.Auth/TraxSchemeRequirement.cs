using Microsoft.AspNetCore.Authorization;

namespace Trax.Api.Auth;

/// <summary>
/// Satisfied only by a principal with an authenticated identity from one of the named schemes.
/// </summary>
/// <remarks>
/// A policy's authentication schemes decide which handlers run when ASP.NET Core authenticates
/// for that policy, but <see cref="IAuthorizationService"/> ignores them: it evaluates the
/// policy's requirements against whatever principal it is handed. A scheme policy such as
/// <c>ApiKeyPolicy</c> therefore carries this requirement, so it means "authenticated by this
/// scheme" wherever it is evaluated: an endpoint gate, <c>GateOperations(policy)</c>,
/// <c>[TraxAuthorize(Policy = ...)]</c>, or a socket.
/// <para>
/// Trax builds every identity with the scheme's name as its authentication type
/// (<c>TraxPrincipal.ToClaimsPrincipal(scheme)</c>), on HTTP and on sockets alike, and that is
/// what is compared. The requirement handles itself, so it needs no registration.
/// </para>
/// <para>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely responsible for
/// securing systems that use it. See SECURITY-DISCLAIMER.md.
/// </para>
/// </remarks>
internal sealed class TraxSchemeRequirement
    : AuthorizationHandler<TraxSchemeRequirement>,
        IAuthorizationRequirement
{
    public TraxSchemeRequirement(params string[] authenticationTypes)
    {
        ArgumentNullException.ThrowIfNull(authenticationTypes);
        AuthenticationTypes = new HashSet<string>(authenticationTypes, StringComparer.Ordinal);
    }

    /// <summary>The identity authentication types that satisfy the requirement.</summary>
    public IReadOnlySet<string> AuthenticationTypes { get; }

    protected override Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        TraxSchemeRequirement requirement
    )
    {
        foreach (var identity in context.User.Identities)
        {
            if (
                identity.IsAuthenticated
                && identity.AuthenticationType is { } type
                && requirement.AuthenticationTypes.Contains(type)
            )
            {
                context.Succeed(requirement);
                break;
            }
        }

        return Task.CompletedTask;
    }

    public override string ToString() =>
        $"TraxSchemeRequirement: authenticated by one of {string.Join(", ", AuthenticationTypes)}";
}
