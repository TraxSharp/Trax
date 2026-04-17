using System.Security.Claims;

namespace Trax.Api.Auth.Jwt;

/// <summary>
/// Default <see cref="ITraxPrincipalResolver{JwtTokenInput}"/> used when the
/// consumer does not supply one. Maps standard JWT claims into a
/// <see cref="TraxPrincipal"/>.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// <para>
/// Claim mapping:
/// <list type="bullet">
/// <item><c>sub</c> (falling back to <see cref="ClaimTypes.NameIdentifier"/>) → <see cref="TraxPrincipal.Id"/>.</item>
/// <item><c>name</c> (falling back to <c>preferred_username</c>, then <c>sub</c>) → <see cref="TraxPrincipal.DisplayName"/>.</item>
/// <item><c>role</c>, <see cref="ClaimTypes.Role"/>, and <c>roles</c> claims → <see cref="TraxPrincipal.Roles"/>.</item>
/// <item>Any claim not in the Trax reserved set is carried verbatim on <see cref="TraxPrincipal.Claims"/>.</item>
/// </list>
/// Returns <c>null</c> when the token has no identifying subject. The handler
/// translates that into <see cref="Microsoft.AspNetCore.Authentication.AuthenticateResult.Fail(string)"/>.
/// </para>
/// </remarks>
public sealed class DefaultJwtPrincipalResolver : ITraxPrincipalResolver<JwtTokenInput>
{
    private static readonly string[] SubjectClaimTypes = ["sub", ClaimTypes.NameIdentifier];

    private static readonly string[] DisplayNameClaimTypes =
    [
        "name",
        ClaimTypes.Name,
        "preferred_username",
    ];

    private static readonly string[] RoleClaimTypes = [ClaimTypes.Role, "role", "roles"];

    private static readonly HashSet<string> ReservedClaimTypes = new(StringComparer.Ordinal)
    {
        "sub",
        ClaimTypes.NameIdentifier,
        "name",
        ClaimTypes.Name,
        "preferred_username",
        ClaimTypes.Role,
        "role",
        "roles",
        TraxAuthClaimTypes.PrincipalId,
        TraxAuthClaimTypes.PrincipalType,
    };

    /// <inheritdoc />
    public ValueTask<TraxPrincipal?> ResolveAsync(JwtTokenInput input, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(input);

        var id = FirstClaim(input.Principal, SubjectClaimTypes);
        if (string.IsNullOrWhiteSpace(id))
            return new ValueTask<TraxPrincipal?>((TraxPrincipal?)null);

        var displayName = FirstClaim(input.Principal, DisplayNameClaimTypes) ?? id;

        var roles = RoleClaimTypes
            .SelectMany(t => input.Principal.FindAll(t))
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var claims = input
            .Principal.Claims.Where(c => !ReservedClaimTypes.Contains(c.Type))
            .GroupBy(c => c.Type, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Value, StringComparer.Ordinal);

        var principal = new TraxPrincipal(
            Id: id,
            DisplayName: displayName,
            Roles: roles,
            Claims: claims.Count > 0 ? claims : null,
            PrincipalType: JwtDefaults.PrincipalType
        );
        return new ValueTask<TraxPrincipal?>(principal);
    }

    private static string? FirstClaim(ClaimsPrincipal principal, IEnumerable<string> claimTypes)
    {
        foreach (var type in claimTypes)
        {
            var value = principal.FindFirst(type)?.Value;
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }
        return null;
    }
}
