using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Trax.Samples.Shared.Auth;

/// <summary>
/// A sample's demo sign-in, Development only (ADR <c>Trax.Docs/adr/0035</c>): a one-hour token for one person, named
/// and given one of the sample's roles, signed with a key published in this repository. Each sample makes one with an
/// issuer and audience of its own, so a token minted for one sample is not accepted by another.
///
/// <para>The key must carry <c>do-not-use-in-production</c>, and the constructor refuses one that does not. Trax.Api
/// refuses to start a host outside Development with such a key registered, so a copied sample cannot carry it into
/// production. <see cref="DemoSignIn"/> registers and maps it in Development only.</para>
/// </summary>
public sealed class DemoTokenIssuer
{
    /// <summary>What every demo signing key contains.</summary>
    public const string Marker = "do-not-use-in-production";

    private readonly byte[] _signingKey;

    /// <param name="issuer">The <c>iss</c> of every token, unique to the sample.</param>
    /// <param name="audience">The <c>aud</c> of every token, unique to the sample.</param>
    /// <param name="signingKeyText">The HMAC key, at least 32 bytes, containing <see cref="Marker"/>.</param>
    /// <param name="roles">The roles a token may carry.</param>
    /// <exception cref="ArgumentException">A value is empty, the key is short or lacks the marker, or no role is given.</exception>
    public DemoTokenIssuer(
        string issuer,
        string audience,
        string signingKeyText,
        IEnumerable<string> roles
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        ArgumentException.ThrowIfNullOrWhiteSpace(signingKeyText);
        ArgumentNullException.ThrowIfNull(roles);
        if (!signingKeyText.Contains(Marker, StringComparison.Ordinal))
            throw new ArgumentException(
                $"A demo signing key must contain '{Marker}', so no host starts with it outside Development.",
                nameof(signingKeyText)
            );
        _signingKey = Encoding.UTF8.GetBytes(signingKeyText);
        if (_signingKey.Length < 32)
            throw new ArgumentException(
                "An HMAC-SHA256 signing key is at least 32 bytes.",
                nameof(signingKeyText)
            );
        Roles = new HashSet<string>(roles, StringComparer.Ordinal);
        if (Roles.Count == 0)
            throw new ArgumentException("A demo issuer needs at least one role.", nameof(roles));
        Issuer = issuer;
        Audience = audience;
    }

    public string Issuer { get; }

    public string Audience { get; }

    /// <summary>The roles a token may carry.</summary>
    public IReadOnlySet<string> Roles { get; }

    /// <summary>The key's bytes, a copy.</summary>
    public byte[] SigningKey => (byte[])_signingKey.Clone();

    /// <summary>A one-hour token for <paramref name="subject"/> with <paramref name="role"/>.</summary>
    /// <exception cref="ArgumentException">The role is not one of <see cref="Roles"/>, or the subject is not a plain name.</exception>
    public string Mint(string subject, string role)
    {
        if (role is null || !Roles.Contains(role))
            throw new ArgumentException($"'{role}' is not a role of this sample.", nameof(role));
        if (
            string.IsNullOrWhiteSpace(subject)
            || subject.Length > 64
            || !subject.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')
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
                    new SymmetricSecurityKey(_signingKey),
                    SecurityAlgorithms.HmacSha256
                ),
            }
        );
    }
}
