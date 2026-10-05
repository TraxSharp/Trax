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
            Claims: new Dictionary<string, string>
            {
                [AuthPolicies.EmailVerifiedClaim] = EmailVerified ? "true" : "false",
            },
            PrincipalType: "apikey"
        );
}

/// <summary>
/// Demo credentials for the Auth sample: four API keys and one HS256 signing key. For
/// demonstration only. They are published in this repository, so anyone who has read this file
/// could sign in as any of these users wherever they are accepted. Program.cs therefore registers
/// every one of them, the JWT signing key included, inside its <c>IsDevelopment()</c> block, and
/// each carries <c>do-not-use-in-production</c>.
/// </summary>
public static class DemoCredentials
{
    /// <summary>Editor with a verified email: may publish.</summary>
    public static readonly DemoUser Alice = new("alice", "Alice", true, AuthRoles.Editor);

    /// <summary>Editor whose email is not verified: has the role, fails the policy.</summary>
    public static readonly DemoUser Erin = new("erin", "Erin", false, AuthRoles.Editor);

    /// <summary>Signed-in reader with no special role.</summary>
    public static readonly DemoUser Bob = new("bob", "Bob", true, AuthRoles.Reader);

    /// <summary>Operator and auditor: uses the operations namespace and reads the audit trail.</summary>
    public static readonly DemoUser Oscar = new(
        "oscar",
        "Oscar",
        true,
        AuthRoles.Operator,
        AuthRoles.Auditor
    );

    /// <summary>Every demo user, by id.</summary>
    public static readonly IReadOnlyDictionary<string, DemoUser> Users = new[]
    {
        Alice,
        Erin,
        Bob,
        Oscar,
    }.ToDictionary(u => u.Id);

    public const string AliceKey = "alice-key-do-not-use-in-production";
    public const string ErinKey = "erin-key-do-not-use-in-production";
    public const string BobKey = "bob-key-do-not-use-in-production";
    public const string OscarKey = "oscar-key-do-not-use-in-production";

    /// <summary>The <c>iss</c> the demo JWT scheme accepts.</summary>
    public const string JwtIssuer = "trax-samples-auth-dev";

    /// <summary>The <c>aud</c> the demo JWT scheme accepts.</summary>
    public const string JwtAudience = "trax-samples-auth";

    /// <summary>The demo HS256 signing key. At least 32 bytes, which <c>UseSymmetricKey</c> requires.</summary>
    public const string JwtSigningKeyText = "auth-sample-signing-key-do-not-use-in-production";

    /// <summary><see cref="JwtSigningKeyText"/> as the bytes <c>UseSymmetricKey</c> takes.</summary>
    public static byte[] JwtSigningKey => Encoding.UTF8.GetBytes(JwtSigningKeyText);

    /// <summary>
    /// Mints a one-hour token for <paramref name="user"/>, signed with the demo key. Claims are the
    /// standard ones Trax's default JWT resolver maps: <c>sub</c> to the id, <c>name</c> to the
    /// display name, <c>role</c> to roles; <c>email_verified</c> lands in the claim bag.
    /// </summary>
    public static string MintToken(DemoUser user)
    {
        var claims = new List<Claim>
        {
            new("sub", user.Id),
            new("name", user.DisplayName),
            new(
                AuthPolicies.EmailVerifiedClaim,
                user.EmailVerified ? "true" : "false",
                ClaimValueTypes.Boolean
            ),
        };
        claims.AddRange(user.Roles.Select(role => new Claim("role", role)));

        return new JsonWebTokenHandler().CreateToken(
            new SecurityTokenDescriptor
            {
                Issuer = JwtIssuer,
                Audience = JwtAudience,
                Subject = new ClaimsIdentity(claims),
                Expires = DateTime.UtcNow.AddHours(1),
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(JwtSigningKey),
                    SecurityAlgorithms.HmacSha256
                ),
            }
        );
    }
}
