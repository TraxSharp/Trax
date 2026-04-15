using System.Diagnostics.CodeAnalysis;
using System.Security.Claims;

namespace Trax.Api.Auth;

/// <summary>
/// Conversions between <see cref="TraxPrincipal"/> and ASP.NET Core's
/// <see cref="ClaimsPrincipal"/>.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// </remarks>
public static class TraxPrincipalExtensions
{
    /// <summary>
    /// Claim types that Trax sets itself from <see cref="TraxPrincipal"/>'s first-class fields
    /// (<see cref="TraxPrincipal.Id"/>, <see cref="TraxPrincipal.DisplayName"/>,
    /// <see cref="TraxPrincipal.Roles"/>, <see cref="TraxPrincipal.PrincipalType"/>). A resolver
    /// must route identity, name, roles, and principal-type through those fields; entries in the
    /// custom <see cref="TraxPrincipal.Claims"/> bag that collide with these keys are silently
    /// dropped in both directions so neither side can forge or duplicate them.
    /// </summary>
    private static readonly HashSet<string> ReservedClaimTypes = new(StringComparer.Ordinal)
    {
        TraxAuthClaimTypes.PrincipalId,
        TraxAuthClaimTypes.PrincipalType,
        ClaimTypes.Name,
        ClaimTypes.Role,
    };

    /// <summary>
    /// Builds a <see cref="ClaimsPrincipal"/> from this <see cref="TraxPrincipal"/>.
    /// Sets <see cref="TraxAuthClaimTypes.PrincipalId"/>, <see cref="ClaimTypes.Name"/>,
    /// one <see cref="ClaimTypes.Role"/> per role, any custom claims verbatim, and
    /// (when specified) <see cref="TraxAuthClaimTypes.PrincipalType"/>. Entries in
    /// <see cref="TraxPrincipal.Claims"/> whose key is a Trax-reserved claim type
    /// (see <see cref="ReservedClaimTypes"/>) are dropped, since those must be set
    /// via the record's first-class fields.
    /// </summary>
    /// <param name="principal">The Trax principal to project.</param>
    /// <param name="authenticationType">
    /// The authentication scheme name. Also sets
    /// <see cref="ClaimsIdentity.AuthenticationType"/>, which is what
    /// ASP.NET Core reads to decide whether the identity is authenticated.
    /// </param>
    public static ClaimsPrincipal ToClaimsPrincipal(
        this TraxPrincipal principal,
        string authenticationType
    )
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentException.ThrowIfNullOrEmpty(authenticationType);

        var claims = new List<Claim>
        {
            new(TraxAuthClaimTypes.PrincipalId, principal.Id),
            new(ClaimTypes.Name, principal.DisplayName),
        };

        foreach (var role in principal.Roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        if (principal.Claims is not null)
        {
            foreach (var (type, value) in principal.Claims)
            {
                if (ReservedClaimTypes.Contains(type))
                    continue;
                claims.Add(new Claim(type, value));
            }
        }

        if (principal.PrincipalType is not null)
            claims.Add(new Claim(TraxAuthClaimTypes.PrincipalType, principal.PrincipalType));

        var identity = new ClaimsIdentity(claims, authenticationType);
        return new ClaimsPrincipal(identity);
    }

    /// <summary>
    /// Attempts to reconstruct a <see cref="TraxPrincipal"/> from a
    /// <see cref="ClaimsPrincipal"/> produced by
    /// <see cref="ToClaimsPrincipal(TraxPrincipal, string)"/>. Returns <c>false</c>
    /// when no <see cref="TraxAuthClaimTypes.PrincipalId"/> claim is present,
    /// meaning the principal did not originate from a Trax auth scheme.
    /// </summary>
    public static bool TryGetTraxPrincipal(
        this ClaimsPrincipal claimsPrincipal,
        [NotNullWhen(true)] out TraxPrincipal? principal
    )
    {
        ArgumentNullException.ThrowIfNull(claimsPrincipal);

        var idClaim = claimsPrincipal.FindFirst(TraxAuthClaimTypes.PrincipalId);
        if (idClaim is null)
        {
            principal = null;
            return false;
        }

        var displayName = claimsPrincipal.FindFirst(ClaimTypes.Name)?.Value ?? idClaim.Value;
        var roles = claimsPrincipal.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray();
        var principalType = claimsPrincipal.FindFirst(TraxAuthClaimTypes.PrincipalType)?.Value;

        var customClaims = claimsPrincipal
            .Claims.Where(c => !ReservedClaimTypes.Contains(c.Type))
            .ToDictionary(c => c.Type, c => c.Value, StringComparer.Ordinal);

        principal = new TraxPrincipal(
            Id: idClaim.Value,
            DisplayName: displayName,
            Roles: roles,
            Claims: customClaims.Count > 0 ? customClaims : null,
            PrincipalType: principalType
        );
        return true;
    }

    /// <summary>
    /// Returns the stable principal identifier from
    /// <see cref="TraxAuthClaimTypes.PrincipalId"/>, or <c>false</c> if the claim
    /// is absent. Convenient for audit sinks and log correlation.
    /// </summary>
    public static bool TryGetPrincipalId(
        this ClaimsPrincipal claimsPrincipal,
        [NotNullWhen(true)] out string? id
    )
    {
        ArgumentNullException.ThrowIfNull(claimsPrincipal);

        id = claimsPrincipal.FindFirst(TraxAuthClaimTypes.PrincipalId)?.Value;
        return id is not null;
    }
}
