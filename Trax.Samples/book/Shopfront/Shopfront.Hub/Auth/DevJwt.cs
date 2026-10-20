using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Shopfront.Auth;

// Tokens for Development only. The host refuses this key in any other environment.
public static class DevJwt
{
    public const string Issuer = "shopfront-dev";
    public const string Audience = "shopfront";

    public static readonly byte[] Key = Encoding.UTF8.GetBytes(
        "shopfront-dev-signing-key-do-not-use-in-production"
    );

    public static string Mint(string user, string role) =>
        new JsonWebTokenHandler().CreateToken(
            new SecurityTokenDescriptor
            {
                Issuer = Issuer,
                Audience = Audience,
                Subject = new ClaimsIdentity(
                    [new Claim("sub", user), new Claim("name", user), new Claim("role", role)]
                ),
                Expires = DateTime.UtcNow.AddHours(1),
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(Key),
                    SecurityAlgorithms.HmacSha256
                ),
            }
        );
}
