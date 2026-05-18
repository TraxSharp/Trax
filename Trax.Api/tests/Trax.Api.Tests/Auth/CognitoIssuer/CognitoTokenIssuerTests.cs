using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Trax.Api.Auth.Jwt.Cognito;
using Trax.Api.Auth.Jwt.Cognito.Issuer;

namespace Trax.Api.Tests.Auth.CognitoIssuer;

[TestFixture]
public class CognitoTokenIssuerTests
{
    private const string Issuer = "https://cognito-idp.us-east-1.amazonaws.com/us-east-1_TestPool";
    private const string ClientId = "abc123clientid";

    private RSA _rsa = null!;
    private RsaSecurityKey _key = null!;

    [SetUp]
    public void SetUp()
    {
        _rsa = RSA.Create(2048);
        _key = new RsaSecurityKey(_rsa) { KeyId = "test-kid-1" };
    }

    [TearDown]
    public void TearDown() => _rsa.Dispose();

    #region Construction

    [Test]
    public void Ctor_NullIssuer_Throws()
    {
        Action act = () => new CognitoTokenIssuer(null!, _key);
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Ctor_EmptyIssuer_Throws()
    {
        Action act = () => new CognitoTokenIssuer("   ", _key);
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void Ctor_NullKey_Throws()
    {
        Action act = () => new CognitoTokenIssuer(Issuer, null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void Ctor_KeyWithoutKid_Throws()
    {
        using var rsa = RSA.Create(2048);
        var keyNoKid = new RsaSecurityKey(rsa); // KeyId is null

        Action act = () => new CognitoTokenIssuer(Issuer, keyNoKid);
        act.Should().Throw<ArgumentException>().WithMessage("*KeyId*");
    }

    #endregion

    #region MintAccessToken

    [Test]
    public void MintAccessToken_NullRequest_Throws()
    {
        var issuer = new CognitoTokenIssuer(Issuer, _key);
        Action act = () => issuer.MintAccessToken(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void MintAccessToken_EmptyClientId_Throws()
    {
        var issuer = new CognitoTokenIssuer(Issuer, _key);
        Action act = () =>
            issuer.MintAccessToken(
                new CognitoAccessTokenRequest
                {
                    Sub = Guid.NewGuid(),
                    ClientId = "",
                    Lifetime = TimeSpan.FromHours(1),
                }
            );
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void MintAccessToken_ZeroLifetime_Throws()
    {
        var issuer = new CognitoTokenIssuer(Issuer, _key);
        Action act = () =>
            issuer.MintAccessToken(
                new CognitoAccessTokenRequest
                {
                    Sub = Guid.NewGuid(),
                    ClientId = ClientId,
                    Lifetime = TimeSpan.Zero,
                }
            );
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void MintAccessToken_NegativeLifetime_Throws()
    {
        var issuer = new CognitoTokenIssuer(Issuer, _key);
        Action act = () =>
            issuer.MintAccessToken(
                new CognitoAccessTokenRequest
                {
                    Sub = Guid.NewGuid(),
                    ClientId = ClientId,
                    Lifetime = TimeSpan.FromMinutes(-1),
                }
            );
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void MintAccessToken_SetsClaimsAndHeader()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddSeconds(1_700_000_000));
        var issuer = new CognitoTokenIssuer(Issuer, _key, clock);
        var sub = Guid.NewGuid();

        var token = issuer.MintAccessToken(
            new CognitoAccessTokenRequest
            {
                Sub = sub,
                ClientId = ClientId,
                Lifetime = TimeSpan.FromHours(1),
                Username = "alice",
                Scopes = new[] { "openid", "profile", "email" },
                Groups = new[] { "admin", "user" },
            }
        );

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Header.Alg.Should().Be(SecurityAlgorithms.RsaSha256);
        jwt.Header.Kid.Should().Be(_key.KeyId);
        jwt.Issuer.Should().Be(Issuer);
        jwt.Subject.Should().Be(sub.ToString());
        jwt.Audiences.Should().BeEmpty("access tokens use client_id, not aud");

        jwt.Claims.Should().ContainSingle(c => c.Type == "token_use" && c.Value == "access");
        jwt.Claims.Should().ContainSingle(c => c.Type == "client_id" && c.Value == ClientId);
        jwt.Claims.Should().ContainSingle(c => c.Type == "username" && c.Value == "alice");
        jwt.Claims.Should()
            .ContainSingle(c => c.Type == "scope" && c.Value == "openid profile email");
        jwt.Claims.Where(c => c.Type == "cognito:groups")
            .Select(c => c.Value)
            .Should()
            .BeEquivalentTo(new[] { "admin", "user" });

        jwt.Claims.Should().ContainSingle(c => c.Type == "jti");
        jwt.Claims.Single(c => c.Type == "iat").Value.Should().Be("1700000000");
        jwt.Claims.Single(c => c.Type == "auth_time").Value.Should().Be("1700000000");
        jwt.Claims.Single(c => c.Type == "exp")
            .Value.Should()
            .Be((1_700_000_000 + 3600).ToString());
    }

    [Test]
    public void MintAccessToken_NoUsername_DefaultsToSub()
    {
        var issuer = new CognitoTokenIssuer(Issuer, _key);
        var sub = Guid.NewGuid();

        var token = issuer.MintAccessToken(
            new CognitoAccessTokenRequest
            {
                Sub = sub,
                ClientId = ClientId,
                Lifetime = TimeSpan.FromMinutes(5),
            }
        );

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Single(c => c.Type == "username").Value.Should().Be(sub.ToString());
    }

    [Test]
    public void MintAccessToken_AuthTime_OverridesIat()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddSeconds(1_700_000_000));
        var issuer = new CognitoTokenIssuer(Issuer, _key, clock);
        var authenticatedAt = DateTimeOffset.UnixEpoch.AddSeconds(1_699_990_000);

        var token = issuer.MintAccessToken(
            new CognitoAccessTokenRequest
            {
                Sub = Guid.NewGuid(),
                ClientId = ClientId,
                Lifetime = TimeSpan.FromHours(1),
                AuthTime = authenticatedAt,
            }
        );

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Single(c => c.Type == "auth_time").Value.Should().Be("1699990000");
        jwt.Claims.Single(c => c.Type == "iat").Value.Should().Be("1700000000");
    }

    [Test]
    public void MintAccessToken_NoGroups_OmitsClaim()
    {
        var issuer = new CognitoTokenIssuer(Issuer, _key);
        var token = issuer.MintAccessToken(
            new CognitoAccessTokenRequest
            {
                Sub = Guid.NewGuid(),
                ClientId = ClientId,
                Lifetime = TimeSpan.FromMinutes(5),
            }
        );

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should().NotContain(c => c.Type == "cognito:groups");
    }

    [Test]
    public void MintAccessToken_NoScopes_OmitsClaim()
    {
        var issuer = new CognitoTokenIssuer(Issuer, _key);
        var token = issuer.MintAccessToken(
            new CognitoAccessTokenRequest
            {
                Sub = Guid.NewGuid(),
                ClientId = ClientId,
                Lifetime = TimeSpan.FromMinutes(5),
            }
        );

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should().NotContain(c => c.Type == "scope");
    }

    [Test]
    public void MintAccessToken_AdditionalClaims_Added()
    {
        var issuer = new CognitoTokenIssuer(Issuer, _key);
        var token = issuer.MintAccessToken(
            new CognitoAccessTokenRequest
            {
                Sub = Guid.NewGuid(),
                ClientId = ClientId,
                Lifetime = TimeSpan.FromMinutes(5),
                AdditionalClaims = new Dictionary<string, string>
                {
                    { "custom:tenant", "acme" },
                    { "device_id", "device-42" },
                },
            }
        );

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Single(c => c.Type == "custom:tenant").Value.Should().Be("acme");
        jwt.Claims.Single(c => c.Type == "device_id").Value.Should().Be("device-42");
    }

    [Test]
    public void MintAccessToken_AdditionalClaim_EmptyKey_Skipped()
    {
        var issuer = new CognitoTokenIssuer(Issuer, _key);
        var token = issuer.MintAccessToken(
            new CognitoAccessTokenRequest
            {
                Sub = Guid.NewGuid(),
                ClientId = ClientId,
                Lifetime = TimeSpan.FromMinutes(5),
                AdditionalClaims = new Dictionary<string, string> { { "  ", "ignored" } },
            }
        );

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should().NotContain(c => c.Value == "ignored");
    }

    [Test]
    public void MintAccessToken_GeneratesUniqueJti()
    {
        var issuer = new CognitoTokenIssuer(Issuer, _key);
        var request = new CognitoAccessTokenRequest
        {
            Sub = Guid.NewGuid(),
            ClientId = ClientId,
            Lifetime = TimeSpan.FromMinutes(5),
        };

        var t1 = issuer.MintAccessToken(request);
        var t2 = issuer.MintAccessToken(request);

        var jti1 = new JwtSecurityTokenHandler()
            .ReadJwtToken(t1)
            .Claims.Single(c => c.Type == "jti")
            .Value;
        var jti2 = new JwtSecurityTokenHandler()
            .ReadJwtToken(t2)
            .Claims.Single(c => c.Type == "jti")
            .Value;
        jti1.Should().NotBe(jti2);
    }

    #endregion

    #region MintIdToken

    [Test]
    public void MintIdToken_EmptyEmail_Throws()
    {
        var issuer = new CognitoTokenIssuer(Issuer, _key);
        Action act = () =>
            issuer.MintIdToken(
                new CognitoIdTokenRequest
                {
                    Sub = Guid.NewGuid(),
                    ClientId = ClientId,
                    Lifetime = TimeSpan.FromHours(1),
                    Email = "",
                    EmailVerified = true,
                }
            );
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void MintIdToken_EmptyClientId_Throws()
    {
        var issuer = new CognitoTokenIssuer(Issuer, _key);
        Action act = () =>
            issuer.MintIdToken(
                new CognitoIdTokenRequest
                {
                    Sub = Guid.NewGuid(),
                    ClientId = "",
                    Lifetime = TimeSpan.FromHours(1),
                    Email = "alice@example.com",
                    EmailVerified = true,
                }
            );
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void MintIdToken_ZeroLifetime_Throws()
    {
        var issuer = new CognitoTokenIssuer(Issuer, _key);
        Action act = () =>
            issuer.MintIdToken(
                new CognitoIdTokenRequest
                {
                    Sub = Guid.NewGuid(),
                    ClientId = ClientId,
                    Lifetime = TimeSpan.Zero,
                    Email = "alice@example.com",
                    EmailVerified = true,
                }
            );
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void MintIdToken_SetsClaimsAndAudience()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddSeconds(1_700_000_000));
        var issuer = new CognitoTokenIssuer(Issuer, _key, clock);
        var sub = Guid.NewGuid();

        var token = issuer.MintIdToken(
            new CognitoIdTokenRequest
            {
                Sub = sub,
                ClientId = ClientId,
                Lifetime = TimeSpan.FromHours(1),
                Email = "alice@example.com",
                EmailVerified = true,
                GivenName = "Alice",
                FamilyName = "Smith",
                Username = "alice",
                Groups = new[] { "admin" },
            }
        );

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Header.Kid.Should().Be(_key.KeyId);
        jwt.Audiences.Should().ContainSingle(a => a == ClientId);
        jwt.Claims.Should().ContainSingle(c => c.Type == "token_use" && c.Value == "id");
        jwt.Claims.Should().ContainSingle(c => c.Type == "email" && c.Value == "alice@example.com");
        jwt.Claims.Should().ContainSingle(c => c.Type == "email_verified" && c.Value == "true");
        jwt.Claims.Should().ContainSingle(c => c.Type == "given_name" && c.Value == "Alice");
        jwt.Claims.Should().ContainSingle(c => c.Type == "family_name" && c.Value == "Smith");
        jwt.Claims.Should().ContainSingle(c => c.Type == "cognito:username" && c.Value == "alice");
        jwt.Claims.Should()
            .NotContain(c => c.Type == "client_id", "ID tokens carry the client id in `aud`");
    }

    [Test]
    public void MintIdToken_EmailVerifiedFalse_SerializesAsFalse()
    {
        var issuer = new CognitoTokenIssuer(Issuer, _key);
        var token = issuer.MintIdToken(
            new CognitoIdTokenRequest
            {
                Sub = Guid.NewGuid(),
                ClientId = ClientId,
                Lifetime = TimeSpan.FromHours(1),
                Email = "u@e.com",
                EmailVerified = false,
            }
        );

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Single(c => c.Type == "email_verified").Value.Should().Be("false");
    }

    [Test]
    public void MintIdToken_NoGivenName_OmitsClaim()
    {
        var issuer = new CognitoTokenIssuer(Issuer, _key);
        var token = issuer.MintIdToken(
            new CognitoIdTokenRequest
            {
                Sub = Guid.NewGuid(),
                ClientId = ClientId,
                Lifetime = TimeSpan.FromHours(1),
                Email = "u@e.com",
                EmailVerified = true,
            }
        );

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should().NotContain(c => c.Type == "given_name");
        jwt.Claims.Should().NotContain(c => c.Type == "family_name");
    }

    [Test]
    public void MintIdToken_NoUsername_DefaultsToSub()
    {
        var issuer = new CognitoTokenIssuer(Issuer, _key);
        var sub = Guid.NewGuid();
        var token = issuer.MintIdToken(
            new CognitoIdTokenRequest
            {
                Sub = sub,
                ClientId = ClientId,
                Lifetime = TimeSpan.FromHours(1),
                Email = "u@e.com",
                EmailVerified = true,
            }
        );

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Single(c => c.Type == "cognito:username").Value.Should().Be(sub.ToString());
    }

    [Test]
    public void MintIdToken_Identities_SerializedAsJsonArrayString()
    {
        var issuer = new CognitoTokenIssuer(Issuer, _key);
        var created = DateTimeOffset.UnixEpoch.AddSeconds(1_700_000_000);
        var token = issuer.MintIdToken(
            new CognitoIdTokenRequest
            {
                Sub = Guid.NewGuid(),
                ClientId = ClientId,
                Lifetime = TimeSpan.FromHours(1),
                Email = "u@e.com",
                EmailVerified = true,
                Identities = new[]
                {
                    new FederatedIdentity(
                        "108222222222",
                        "Google",
                        "Google",
                        Primary: true,
                        DateCreated: created
                    ),
                },
            }
        );

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var identitiesClaim = jwt.Claims.Single(c => c.Type == "identities");
        identitiesClaim.Value.Should().StartWith("[").And.EndWith("]");

        using var doc = JsonDocument.Parse(identitiesClaim.Value);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().Be(1);
        var entry = doc.RootElement[0];
        entry.GetProperty("userId").GetString().Should().Be("108222222222");
        entry.GetProperty("providerName").GetString().Should().Be("Google");
        entry.GetProperty("providerType").GetString().Should().Be("Google");
        entry.GetProperty("primary").GetBoolean().Should().BeTrue();
        entry
            .GetProperty("dateCreated")
            .GetString()
            .Should()
            .Be((created.ToUnixTimeMilliseconds()).ToString());
    }

    [Test]
    public void MintIdToken_NoIdentities_OmitsClaim()
    {
        var issuer = new CognitoTokenIssuer(Issuer, _key);
        var token = issuer.MintIdToken(
            new CognitoIdTokenRequest
            {
                Sub = Guid.NewGuid(),
                ClientId = ClientId,
                Lifetime = TimeSpan.FromHours(1),
                Email = "u@e.com",
                EmailVerified = true,
            }
        );

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Should().NotContain(c => c.Type == "identities");
    }

    [Test]
    public void MintIdToken_AuthTime_OverridesIat()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddSeconds(1_700_000_000));
        var issuer = new CognitoTokenIssuer(Issuer, _key, clock);
        var authenticatedAt = DateTimeOffset.UnixEpoch.AddSeconds(1_699_990_000);

        var token = issuer.MintIdToken(
            new CognitoIdTokenRequest
            {
                Sub = Guid.NewGuid(),
                ClientId = ClientId,
                Lifetime = TimeSpan.FromHours(1),
                Email = "u@e.com",
                EmailVerified = true,
                AuthTime = authenticatedAt,
            }
        );

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        jwt.Claims.Single(c => c.Type == "auth_time").Value.Should().Be("1699990000");
    }

    #endregion

    #region SignatureValidity

    [Test]
    public void MintAccessToken_SignatureValidatesAgainstPublicKey()
    {
        var issuer = new CognitoTokenIssuer(Issuer, _key);
        var token = issuer.MintAccessToken(
            new CognitoAccessTokenRequest
            {
                Sub = Guid.NewGuid(),
                ClientId = ClientId,
                Lifetime = TimeSpan.FromHours(1),
            }
        );

        var handler = new JwtSecurityTokenHandler();
        var tvp = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = false,
            ValidateLifetime = true,
            IssuerSigningKey = _key,
            ValidateIssuerSigningKey = true,
        };
        var act = () => handler.ValidateToken(token, tvp, out _);
        act.Should().NotThrow();
    }

    [Test]
    public void MintIdToken_SignatureValidatesAgainstPublicKey()
    {
        var issuer = new CognitoTokenIssuer(Issuer, _key);
        var token = issuer.MintIdToken(
            new CognitoIdTokenRequest
            {
                Sub = Guid.NewGuid(),
                ClientId = ClientId,
                Lifetime = TimeSpan.FromHours(1),
                Email = "u@e.com",
                EmailVerified = true,
            }
        );

        var handler = new JwtSecurityTokenHandler();
        var tvp = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = Issuer,
            ValidateAudience = true,
            ValidAudience = ClientId,
            ValidateLifetime = true,
            IssuerSigningKey = _key,
            ValidateIssuerSigningKey = true,
        };
        var act = () => handler.ValidateToken(token, tvp, out _);
        act.Should().NotThrow();
    }

    [Test]
    public void MintAccessToken_TamperedSignature_FailsValidation()
    {
        var issuer = new CognitoTokenIssuer(Issuer, _key);
        var token = issuer.MintAccessToken(
            new CognitoAccessTokenRequest
            {
                Sub = Guid.NewGuid(),
                ClientId = ClientId,
                Lifetime = TimeSpan.FromHours(1),
            }
        );

        // Swap the signature segment for a different valid-looking string.
        var parts = token.Split('.');
        parts[2] =
            parts[2].Length > 4
                ? parts[2][..^4] + (parts[2][^4..] == "AAAA" ? "BBBB" : "AAAA")
                : "AAAA";
        var tampered = string.Join('.', parts);

        var handler = new JwtSecurityTokenHandler();
        var tvp = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = false,
            IssuerSigningKey = _key,
            ValidateIssuerSigningKey = true,
        };
        var act = () => handler.ValidateToken(tampered, tvp, out _);
        act.Should().Throw<SecurityTokenException>();
    }

    #endregion

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
