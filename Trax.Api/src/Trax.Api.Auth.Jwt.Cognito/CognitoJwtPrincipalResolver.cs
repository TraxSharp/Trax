using System.Security.Claims;
using System.Text.Json;

namespace Trax.Api.Auth.Jwt.Cognito;

/// <summary>
/// <see cref="ITraxPrincipalResolver{JwtTokenInput}"/> tuned for Amazon
/// Cognito tokens. Normalizes Cognito-specific claims so downstream code
/// (authorization, audit, business logic) sees a consistent
/// <see cref="TraxPrincipal"/> shape regardless of whether the user signed
/// in with a password or federated through Google/Apple.
/// </summary>
/// <remarks>
/// NO WARRANTY. Trax auth is plumbing, not a security product. You are solely
/// responsible for securing systems that use it. See SECURITY-DISCLAIMER.md.
/// <para>Claim handling, beyond the base <see cref="DefaultJwtPrincipalResolver"/>:</para>
/// <list type="bullet">
/// <item><c>cognito:groups</c> claims merge into <see cref="TraxPrincipal.Roles"/>.</item>
/// <item><c>cognito:username</c> participates in display-name fallback.</item>
/// <item><c>identities</c> JSON array is parsed; the primary provider's
/// <c>providerName</c> surfaces on the principal as the synthetic
/// <see cref="CognitoDefaults.IdentityProvider"/> claim.</item>
/// <item>Native users (no <c>identities</c> claim) get
/// <see cref="CognitoDefaults.IdentityProvider"/> = <c>cognito</c>.</item>
/// <item>The <see cref="TraxPrincipal.PrincipalType"/> discriminator is
/// <see cref="CognitoDefaults.PrincipalType"/>.</item>
/// </list>
/// <para>
/// Apple's second-and-later logins omit <c>email</c>; this resolver tolerates
/// missing email (the principal is still valid because <c>sub</c> is present).
/// Hosts that need the email persisted across logins should look it up from
/// their own database keyed by <c>sub</c>.
/// </para>
/// </remarks>
public sealed class CognitoJwtPrincipalResolver : ITraxPrincipalResolver<JwtTokenInput>
{
    private static readonly string[] SubjectClaimTypes = ["sub", ClaimTypes.NameIdentifier];

    private static readonly string[] DisplayNameClaimTypes =
    [
        "name",
        ClaimTypes.Name,
        "preferred_username",
        CognitoDefaults.CognitoUsername,
        CognitoDefaults.Email,
        ClaimTypes.Email,
    ];

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
        CognitoDefaults.CognitoUsername,
        CognitoDefaults.CognitoGroups,
        CognitoDefaults.Identities,
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

        var roles = input
            .Principal.FindAll(ClaimTypes.Role)
            .Concat(input.Principal.FindAll("role"))
            .Concat(input.Principal.FindAll("roles"))
            .Concat(input.Principal.FindAll(CognitoDefaults.CognitoGroups))
            .Select(c => c.Value)
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        var claims = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var c in input.Principal.Claims)
        {
            if (ReservedClaimTypes.Contains(c.Type))
                continue;
            // First write wins to avoid duplicate-key throws while preserving
            // the resolver's first-occurrence semantics.
            claims.TryAdd(c.Type, c.Value);
        }

        var identityProvider = ExtractIdentityProvider(input.Principal);
        claims[CognitoDefaults.IdentityProvider] = identityProvider;

        var principal = new TraxPrincipal(
            Id: id,
            DisplayName: displayName,
            Roles: roles,
            Claims: claims.Count > 0 ? claims : null,
            PrincipalType: CognitoDefaults.PrincipalType
        );
        return new ValueTask<TraxPrincipal?>(principal);
    }

    /// <summary>
    /// Returns the primary federated provider name from the
    /// <c>identities</c> claim, or <c>"cognito"</c> for native users.
    /// Surface defaults that don't break callers: malformed JSON, an empty
    /// array, or a missing <c>providerName</c> field all resolve to
    /// <c>"cognito"</c>.
    /// </summary>
    internal static string ExtractIdentityProvider(ClaimsPrincipal principal)
    {
        var identitiesClaim = principal.FindFirst(CognitoDefaults.Identities);
        if (identitiesClaim is null)
            return CognitoDefaults.PrincipalType;

        try
        {
            using var doc = JsonDocument.Parse(WrapIfNeeded(identitiesClaim.Value));
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return CognitoDefaults.PrincipalType;

            // Prefer the entry flagged "primary": true; otherwise pick the
            // first entry. Cognito normally only emits one identity per
            // federated user, but the format is an array.
            JsonElement? chosen = null;
            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object)
                    continue;
                if (chosen is null)
                    chosen = entry;
                if (entry.TryGetProperty("primary", out var primary) && IsTruthy(primary))
                {
                    chosen = entry;
                    break;
                }
            }

            if (
                chosen is { } picked
                && picked.TryGetProperty("providerName", out var providerName)
                && providerName.ValueKind == JsonValueKind.String
                && providerName.GetString() is { Length: > 0 } name
            )
            {
                return name;
            }
        }
        catch (JsonException)
        {
            // Fall through.
        }

        return CognitoDefaults.PrincipalType;
    }

    /// <summary>
    /// Cognito serializes the <c>identities</c> claim as a JSON array when
    /// emitted by the user pool, but some downstream serializers (mobile
    /// SDKs, test fixtures) re-emit the same data wrapped in an outer object
    /// or stringified. Accept both shapes.
    /// </summary>
    private static string WrapIfNeeded(string raw)
    {
        var trimmed = raw.TrimStart();
        return trimmed.StartsWith('[') ? raw : "[" + raw + "]";
    }

    private static bool IsTruthy(JsonElement element) =>
        element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.String => string.Equals(
                element.GetString(),
                "true",
                StringComparison.OrdinalIgnoreCase
            ),
            _ => false,
        };

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
