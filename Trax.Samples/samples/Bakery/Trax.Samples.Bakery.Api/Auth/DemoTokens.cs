using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Trax.Samples.Bakery.Api.Auth;

/// <summary>
/// The demo's sign-in, Development only (ADR <c>Trax.Docs/adr/0035</c>): a token for one person, named and given one
/// role, signed with a key published in this repository. The page asks for one per person, so no person's credential
/// is in another's page, and the feature tests mint one per person per scenario. The key carries
/// <c>do-not-use-in-production</c>, so a host using it refuses to start outside Development.
/// </summary>
public static class DemoTokens
{
    public const string Issuer = "trax-samples-bakery-dev";
    public const string Audience = "trax-samples-bakery";
    public const string SigningKeyText = "bakery-sample-signing-key-do-not-use-in-production";

    public static byte[] SigningKey => Encoding.UTF8.GetBytes(SigningKeyText);

    /// <summary>A one-hour token for <paramref name="subject"/> with <paramref name="role"/>.</summary>
    /// <exception cref="ArgumentException">The role is not a bakery role, or the subject is not a plain name.</exception>
    public static string Mint(string subject, string role)
    {
        if (!BakeryRoles.All.Contains(role))
            throw new ArgumentException($"'{role}' is not a bakery role.", nameof(role));
        if (
            string.IsNullOrWhiteSpace(subject)
            || subject.Length > 64
            || !subject.All(c => char.IsLetterOrDigit(c) || c is '-' or '_')
        )
            throw new ArgumentException(
                "A name is 1 to 64 letters, digits, dashes or underscores.",
                nameof(subject)
            );
        return new JsonWebTokenHandler().CreateToken(
            new SecurityTokenDescriptor
            {
                Issuer = Issuer,
                Audience = Audience,
                Subject = new ClaimsIdentity([
                    new("sub", subject),
                    new("name", subject),
                    new("role", role),
                ]),
                Expires = DateTime.UtcNow.AddHours(1),
                SigningCredentials = new SigningCredentials(
                    new SymmetricSecurityKey(SigningKey),
                    SecurityAlgorithms.HmacSha256
                ),
            }
        );
    }
}
