using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.IdentityModel.Tokens;

namespace Trax.Api.Auth.Jwt.Testing;

/// <summary>
/// Fluent builder for an individual JWT minted by <see cref="TestTokenIssuer"/>.
/// </summary>
public sealed class TestTokenBuilder
{
    private readonly List<Claim> _claims = new();
    private readonly TimeProvider _clock;

    internal TestTokenBuilder(string issuer, string audience, TimeProvider clock)
    {
        _clock = clock;
        IssuerValue = issuer;
        AudienceValue = audience;
        var now = clock.GetUtcNow().UtcDateTime;
        NotBeforeValue = now.AddMinutes(-1);
        ExpiresValue = now.AddMinutes(5);
    }

    internal string IssuerValue { get; private set; }
    internal string AudienceValue { get; private set; }
    internal DateTime NotBeforeValue { get; private set; }
    internal DateTime ExpiresValue { get; private set; }

    /// <summary>Sets the <c>sub</c> claim.</summary>
    public TestTokenBuilder WithSubject(string sub)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sub);
        _claims.RemoveAll(c => string.Equals(c.Type, "sub", StringComparison.Ordinal));
        _claims.Add(new Claim("sub", sub));
        return this;
    }

    /// <summary>Appends a claim. Multiple calls with the same type are allowed.</summary>
    public TestTokenBuilder WithClaim(string type, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(value);
        _claims.Add(new Claim(type, value));
        return this;
    }

    /// <summary>Appends a role claim (<see cref="ClaimTypes.Role"/>).</summary>
    public TestTokenBuilder WithRole(string role)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);
        _claims.Add(new Claim(ClaimTypes.Role, role));
        return this;
    }

    /// <summary>Overrides the issuer (<c>iss</c>) for this token only.</summary>
    public TestTokenBuilder WithIssuer(string issuer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(issuer);
        IssuerValue = issuer;
        return this;
    }

    /// <summary>Overrides the audience (<c>aud</c>) for this token only.</summary>
    public TestTokenBuilder WithAudience(string audience)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(audience);
        AudienceValue = audience;
        return this;
    }

    /// <summary>Sets the not-before timestamp.</summary>
    public TestTokenBuilder WithNotBefore(DateTime notBefore)
    {
        NotBeforeValue = notBefore;
        return this;
    }

    /// <summary>Sets the expiration timestamp.</summary>
    public TestTokenBuilder WithExpires(DateTime expires)
    {
        ExpiresValue = expires;
        return this;
    }

    /// <summary>Sets the lifetime relative to now (nbf = now-1min, exp = now+lifetime).</summary>
    public TestTokenBuilder WithLifetime(TimeSpan lifetime)
    {
        if (lifetime <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(lifetime), "Lifetime must be positive.");
        var now = _clock.GetUtcNow().UtcDateTime;
        NotBeforeValue = now.AddMinutes(-1);
        ExpiresValue = now.Add(lifetime);
        return this;
    }

    internal IEnumerable<Claim> GetClaims() => _claims;
}
