using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Trax.Samples.GameServer.Auth;

namespace Trax.Samples.GameServer.Api;

/// <summary>
/// Demo-only JWT issuers for the GameServer sample. Two symmetric HS256 schemes
/// stand in for a game client's own session tokens ("player") and a partner
/// service's tokens ("partner"). <c>AddTraxJwtDispatcher</c> routes each to its
/// scheme by the token's <c>iss</c> claim, over both HTTP and WebSocket
/// subscriptions.
/// </summary>
/// <remarks>
/// For demonstration only. The signing keys are plaintext constants published in
/// the Trax repository. Any production system that ships them is broken. See
/// <c>Trax.Api/SECURITY-DISCLAIMER.md</c>.
/// </remarks>
public static class DemoJwt
{
    /// <summary>Audience both demo schemes validate against.</summary>
    public const string Audience = "trax-gameserver";

    /// <summary>Scheme name for the game client's own session tokens.</summary>
    public const string PlayerScheme = "player";

    /// <summary><c>iss</c> value the dispatcher maps to <see cref="PlayerScheme"/>.</summary>
    public const string PlayerIssuer = "trax-gameserver-player";

    /// <summary>Scheme name for a partner service's tokens.</summary>
    public const string PartnerScheme = "partner";

    /// <summary><c>iss</c> value the dispatcher maps to <see cref="PartnerScheme"/>.</summary>
    public const string PartnerIssuer = "trax-gameserver-partner";

    /// <summary>Demo HS256 key for the player scheme (32 bytes, demo only).</summary>
    public static readonly byte[] PlayerKey = Encoding.UTF8.GetBytes(
        "gameserver-player-demo-signkey!!"
    );

    /// <summary>Demo HS256 key for the partner scheme (32 bytes, demo only).</summary>
    public static readonly byte[] PartnerKey = Encoding.UTF8.GetBytes(
        "gameserver-partner-demo-signkey!"
    );

    /// <summary>Mints a player-issuer token carrying the <c>Player</c> role.</summary>
    public static string MintPlayer(string sub = "player-1", string name = "Ada Player") =>
        Mint(PlayerIssuer, PlayerKey, sub, name, nameof(GameRole.Player));

    /// <summary>Mints a partner-issuer token carrying the <c>Player</c> role.</summary>
    public static string MintPartner(string sub = "partner-svc", string name = "Partner Service") =>
        Mint(PartnerIssuer, PartnerKey, sub, name, nameof(GameRole.Player));

    private static string Mint(
        string issuer,
        byte[] key,
        string sub,
        string name,
        params string[] roles
    )
    {
        var claims = new List<Claim> { new("sub", sub), new("name", name) };
        foreach (var role in roles)
            claims.Add(new Claim("role", role));

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = Audience,
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256
            ),
        };

        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
