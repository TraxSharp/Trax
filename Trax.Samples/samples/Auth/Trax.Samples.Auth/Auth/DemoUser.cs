using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Trax.Api.Auth;

namespace Trax.Samples.Auth.Auth;

/// <summary>
/// One demo user, who can sign in with an API key or with a JWT. The same person reaches the
/// host through two schemes, so the principal ids differ only by the scheme prefix
/// (<c>TraxApiKey:alice</c>, <c>TraxJwt:alice</c>).
/// </summary>
public sealed record DemoUser(
    string Id,
    string DisplayName,
    bool EmailVerified,
    params string[] Roles
)
{
    /// <summary>
    /// The principal the API-key scheme resolves this user's key to. The factory overload of
    /// <c>ApiKeyBuilder.Add</c> leaves <c>PrincipalType</c> to the caller, so it is set here to the
    /// value the plain <c>Add(key, id, roles)</c> overload uses.
    /// </summary>
    public TraxPrincipal ToApiKeyPrincipal() =>
        new(
            Id,
            DisplayName,
            Roles,
            Claims: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [AuthPolicies.EmailVerifiedClaim] = EmailVerified ? "true" : "false",
            },
            PrincipalType: "apikey"
        );
}
