using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Microsoft.IdentityModel.Tokens;
using Trax.Api.Auth.Jwt.Testing;

namespace Trax.Api.Tests.Auth.Testing;

[TestFixture]
public class TestJwksServerMultiKeyTests
{
    #region AddSigningKey

    [Test]
    public async Task AddSigningKey_PublishesNewKeyInJwks()
    {
        await using var server = await TestJwksServer.StartAsync();
        var originalKid = server.SigningKey.KeyId;

        var newKid = server.AddSigningKey();

        server.SigningKeyIds.Should().Contain(originalKid).And.Contain(newKid);

        using var http = new HttpClient();
        var jwks = await http.GetStringAsync(server.JwksUri);
        using var doc = JsonDocument.Parse(jwks);
        var publishedKids = doc
            .RootElement.GetProperty("keys")
            .EnumerateArray()
            .Select(k => k.GetProperty("kid").GetString())
            .ToArray();
        publishedKids.Should().BeEquivalentTo(new[] { originalKid, newKid });
    }

    [Test]
    public async Task AddSigningKey_MakesNewKeyCurrent()
    {
        await using var server = await TestJwksServer.StartAsync();
        var originalKid = server.SigningKey.KeyId;

        var newKid = server.AddSigningKey();

        server.SigningKey.KeyId.Should().Be(newKid);
        server.SigningKey.KeyId.Should().NotBe(originalKid);
    }

    [Test]
    public async Task AddSigningKey_WithExternalRsa_Used()
    {
        await using var server = await TestJwksServer.StartAsync();
        using var rsa = RSA.Create(2048);
        var kid = server.AddSigningKey(rsa);

        server
            .SigningCredentialsByKid[kid]
            .Key.Should()
            .BeOfType<RsaSecurityKey>()
            .Which.Rsa.Should()
            .BeSameAs(rsa);
    }

    [Test]
    public async Task TokenSignedWithRotatedKey_ValidatesAgainstThatKid()
    {
        await using var server = await TestJwksServer.StartAsync();
        var firstKid = server.SigningKey.KeyId;
        var secondKid = server.AddSigningKey();

        var issuer = server.CreateIssuer("trax-aud");
        var firstToken = issuer.WithSigningKey(firstKid).Mint(b => b.WithSubject("user1"));
        var secondToken = issuer.WithSigningKey(secondKid).Mint(b => b.WithSubject("user2"));

        new JwtSecurityTokenHandler().ReadJwtToken(firstToken).Header.Kid.Should().Be(firstKid);
        new JwtSecurityTokenHandler().ReadJwtToken(secondToken).Header.Kid.Should().Be(secondKid);

        ValidateRs256(
            firstToken,
            server.Issuer,
            "trax-aud",
            server.SigningCredentialsByKid[firstKid].Key
        );
        ValidateRs256(
            secondToken,
            server.Issuer,
            "trax-aud",
            server.SigningCredentialsByKid[secondKid].Key
        );
    }

    #endregion

    #region RemoveSigningKey

    [Test]
    public async Task RemoveSigningKey_RemovesFromJwks()
    {
        await using var server = await TestJwksServer.StartAsync();
        var secondKid = server.AddSigningKey();
        var firstKid = server.SigningKeyIds.Single(k => k != secondKid);

        var removed = server.RemoveSigningKey(secondKid);

        removed.Should().BeTrue();
        server.SigningKeyIds.Should().NotContain(secondKid).And.Contain(firstKid);
    }

    [Test]
    public async Task RemoveSigningKey_PromotesSurvivingKeyToCurrent()
    {
        await using var server = await TestJwksServer.StartAsync();
        var firstKid = server.SigningKey.KeyId;
        var secondKid = server.AddSigningKey();
        server.SigningKey.KeyId.Should().Be(secondKid);

        server.RemoveSigningKey(secondKid);

        server.SigningKey.KeyId.Should().Be(firstKid, "the surviving key is promoted to current");
    }

    [Test]
    public async Task RemoveSigningKey_UnknownKid_ReturnsFalse()
    {
        await using var server = await TestJwksServer.StartAsync();
        server.RemoveSigningKey("not-a-real-kid").Should().BeFalse();
    }

    [Test]
    public async Task RemoveSigningKey_EmptyKid_Throws()
    {
        await using var server = await TestJwksServer.StartAsync();
        Action act = () => server.RemoveSigningKey("  ");
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public async Task RemoveAllKeys_NextSigningKeyAccess_ThrowsClearly()
    {
        await using var server = await TestJwksServer.StartAsync();
        var kid = server.SigningKey.KeyId;

        server.RemoveSigningKey(kid);

        Action act = () => _ = server.SigningKey;
        act.Should().Throw<InvalidOperationException>().WithMessage("*no current signing key*");
    }

    #endregion

    #region Options

    [Test]
    public async Task StartAsync_WithIssuerOverride_UsesIt()
    {
        var custom = "https://cognito-idp.us-east-1.amazonaws.com/us-east-1_FakePool";
        await using var server = await TestJwksServer.StartAsync(
            new TestJwksServerOptions { IssuerOverride = custom }
        );

        server.Issuer.Should().Be(custom);
        server.JwksUri.Should().Be(custom + "/.well-known/jwks.json");
    }

    [Test]
    public async Task StartAsync_WithPathPrefix_MountsEndpointsAtPrefix()
    {
        await using var server = await TestJwksServer.StartAsync(
            new TestJwksServerOptions { PathPrefix = "/local_us-east-1_xxx" }
        );

        server.Issuer.Should().EndWith("/local_us-east-1_xxx");
        server.JwksUri.Should().EndWith("/local_us-east-1_xxx/.well-known/jwks.json");

        using var http = new HttpClient();
        var resp = await http.GetAsync(server.JwksUri);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var discovery = await http.GetStringAsync(
            server.Issuer + "/.well-known/openid-configuration"
        );
        using var doc = JsonDocument.Parse(discovery);
        doc.RootElement.GetProperty("issuer").GetString().Should().Be(server.Issuer);
    }

    [Test]
    public void StartAsync_PathPrefixWithoutLeadingSlash_Throws()
    {
        Func<Task> act = async () =>
            await TestJwksServer.StartAsync(new TestJwksServerOptions { PathPrefix = "bad" });
        act.Should().ThrowAsync<ArgumentException>().WithMessage("*'/' when non-empty*");
    }

    [Test]
    public void StartAsync_PathPrefixWithTrailingSlash_Throws()
    {
        Func<Task> act = async () =>
            await TestJwksServer.StartAsync(new TestJwksServerOptions { PathPrefix = "/bad/" });
        act.Should().ThrowAsync<ArgumentException>().WithMessage("*not end with*");
    }

    #endregion

    #region TestTokenIssuer.WithSigningKey

    [Test]
    public async Task WithSigningKey_UnknownKid_Throws()
    {
        await using var server = await TestJwksServer.StartAsync();
        var issuer = server.CreateIssuer("aud");

        Action act = () => issuer.WithSigningKey("nope");
        act.Should().Throw<ArgumentException>().WithMessage("*kid 'nope'*");
    }

    [Test]
    public async Task WithSigningKey_EmptyKid_Throws()
    {
        await using var server = await TestJwksServer.StartAsync();
        var issuer = server.CreateIssuer("aud");

        Action act = () => issuer.WithSigningKey("  ");
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void WithSigningKey_OnStandaloneIssuer_Throws()
    {
        // An issuer constructed without a keyset can't switch keys.
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "k1" };
        var creds = new SigningCredentials(key, SecurityAlgorithms.RsaSha256);
        var issuer = new TestTokenIssuer("iss", "aud", creds);

        Action act = () => issuer.WithSigningKey("k1");
        act.Should().Throw<InvalidOperationException>().WithMessage("*keyset*");
    }

    #endregion

    private static void ValidateRs256(string token, string issuer, string audience, SecurityKey key)
    {
        var handler = new JwtSecurityTokenHandler();
        var tvp = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateLifetime = true,
            IssuerSigningKey = key,
            ValidateIssuerSigningKey = true,
        };
        var act = () => handler.ValidateToken(token, tvp, out _);
        act.Should().NotThrow();
    }
}
