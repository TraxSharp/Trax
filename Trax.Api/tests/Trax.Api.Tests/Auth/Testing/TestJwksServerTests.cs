using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Trax.Api.Auth.Jwt;
using Trax.Api.Auth.Jwt.Testing;

namespace Trax.Api.Tests.Auth.Testing;

[TestFixture]
public class TestJwksServerTests
{
    [Test]
    public async Task StartAsync_StartsServerOnLoopback()
    {
        await using var server = await TestJwksServer.StartAsync();

        server.Issuer.Should().StartWith("http://127.0.0.1:");
        server.JwksUri.Should().EndWith("/.well-known/jwks.json");
        server.SigningKey.Should().NotBeNull();
        server.SigningCredentials.Algorithm.Should().Be(SecurityAlgorithms.RsaSha256);
    }

    [Test]
    public async Task JwksEndpoint_ReturnsExpectedShape()
    {
        await using var server = await TestJwksServer.StartAsync();
        using var http = new HttpClient();

        var resp = await http.GetAsync(server.JwksUri);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        resp.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        var keys = doc.RootElement.GetProperty("keys");
        keys.GetArrayLength().Should().Be(1);
        var k = keys[0];
        k.GetProperty("kty").GetString().Should().Be("RSA");
        k.GetProperty("alg").GetString().Should().Be("RS256");
        k.GetProperty("use").GetString().Should().Be("sig");
        k.GetProperty("kid").GetString().Should().NotBeNullOrEmpty();
        k.GetProperty("n").GetString().Should().NotBeNullOrEmpty();
        k.GetProperty("e").GetString().Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task DiscoveryEndpoint_ReturnsIssuerAndJwksUri()
    {
        await using var server = await TestJwksServer.StartAsync();
        using var http = new HttpClient();

        var resp = await http.GetAsync(server.Issuer + "/.well-known/openid-configuration");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        doc.RootElement.GetProperty("issuer").GetString().Should().Be(server.Issuer);
        doc.RootElement.GetProperty("jwks_uri").GetString().Should().Be(server.JwksUri);
    }

    [Test]
    public async Task CreateIssuer_ProducesValidatableRs256Tokens()
    {
        await using var server = await TestJwksServer.StartAsync();
        var issuer = server.CreateIssuer("trax-aud");
        var token = issuer.Mint(b => b.WithSubject("alice"));

        // Verify signature with the published public key.
        var handler = new JwtSecurityTokenHandler();
        var jwt = handler.ReadJwtToken(token);
        jwt.Header.Alg.Should().Be("RS256");
        jwt.Header.Kid.Should().Be(server.SigningKey.KeyId);

        var validationParams = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = server.Issuer,
            ValidateAudience = true,
            ValidAudience = "trax-aud",
            ValidateLifetime = true,
            IssuerSigningKey = server.SigningKey,
            ValidateIssuerSigningKey = true,
        };
        var validated = handler.ValidateToken(token, validationParams, out _);
        validated.FindFirst("sub")?.Value.Should().Be("alice");
    }

    [Test]
    public async Task EndToEnd_TokenValidatedByJwtBearer()
    {
        await using var server = await TestJwksServer.StartAsync();

        using var host = await BuildHost(server);
        var client = host.GetTestClient();

        var token = server.CreateIssuer("trax-aud").Mint(b => b.WithSubject("alice"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var resp = await client.GetAsync("/protected");
        resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await resp.Content.ReadAsStringAsync();
        body.Should().Contain("alice");
    }

    [Test]
    public async Task DisposeAsync_ShutsDownServer()
    {
        var server = await TestJwksServer.StartAsync();
        var jwksUri = server.JwksUri;
        await server.DisposeAsync();

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        Func<Task> act = async () => await http.GetAsync(jwksUri);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Test]
    public async Task TwoServers_HaveDistinctIssuersAndKeys()
    {
        await using var a = await TestJwksServer.StartAsync();
        await using var b = await TestJwksServer.StartAsync();

        a.Issuer.Should().NotBe(b.Issuer);
        a.SigningKey.KeyId.Should().NotBe(b.SigningKey.KeyId);
    }

    private static async Task<IHost> BuildHost(TestJwksServer server)
    {
        var host = new HostBuilder()
            .ConfigureWebHost(web =>
                web.UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddRouting();
                        services.AddLogging();
                        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment());
                        services.AddTraxJwtAuth(jwt =>
                            jwt.UseAuthority(server.Issuer, "trax-aud").AllowHttpMetadata()
                        );
                    })
                    .Configure(app =>
                    {
                        app.UseRouting();
                        app.UseAuthentication();
                        app.UseAuthorization();
                        app.UseEndpoints(endpoints =>
                        {
                            endpoints
                                .MapGet(
                                    "/protected",
                                    (ClaimsPrincipal user) =>
                                        Results.Ok(user.FindFirst("trax:principal-id")?.Value)
                                )
                                .RequireAuthorization(JwtDefaults.PolicyName);
                        });
                    })
            )
            .Build();
        await host.StartAsync();
        return host;
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
