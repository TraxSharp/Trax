using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using Microsoft.IdentityModel.Tokens;
using static Trax.Api.Tests.AuthE2E.AuthE2EHost;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// End-to-end coverage for HTTP queries and mutations against the real Trax
/// GraphQL endpoint, exercising the full pipeline: auth middleware → Trax
/// resolver → TraxPrincipal → ClaimsPrincipal → GraphQL execution → train
/// run. No mocks in the auth path.
///
/// Tests talk to the Postgres instance started by docker-compose.
/// </summary>
[TestFixture]
[NonParallelizable]
public class HttpAuthE2ETests
{
    private const string Database = "trax_api_auth_http";

    private static Task<Microsoft.Extensions.Hosting.IHost> StartAsync(Schemes s) =>
        AuthE2EHost.StartAsync(s, Database);

    private const string EchoQuery = """
        query Echo {
          discover { audit { echo(input: { message: "hi" }) { reply } } }
        }
        """;

    private const string NotifyMutation = """
        mutation Notify {
          dispatch { audit { notify(input: { topic: "t", body: "b" }) { externalId } } }
        }
        """;

    // ── API key scheme ───────────────────────────────────────────────────

    [Test]
    public async Task Query_WithValidApiKey_Succeeds()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            EchoQuery,
            req => req.Headers.Add("X-Api-Key", PlayerApiKey)
        );

        doc.RootElement.TryGetProperty("errors", out _).Should().BeFalse();
        var reply = doc
            .RootElement.GetProperty("data")
            .GetProperty("discover")
            .GetProperty("audit")
            .GetProperty("echo")
            .GetProperty("reply")
            .GetString();
        reply.Should().Be("echo: hi");
    }

    [Test]
    public async Task Query_WithInvalidApiKey_OnProtectedEndpoint_Returns401()
    {
        using var host = await StartAsync(Schemes.ApiKey);
        var client = host.GetTestServer().CreateClient();

        using var req = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql/protected")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { query = EchoQuery }),
        };
        req.Headers.Add("X-Api-Key", "bogus-key");

        var res = await client.SendAsync(req);

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Query_WithMissingApiKey_OnProtectedEndpoint_Returns401()
    {
        using var host = await StartAsync(Schemes.ApiKey);
        var client = host.GetTestServer().CreateClient();

        using var req = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql/protected")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { query = EchoQuery }),
        };

        var res = await client.SendAsync(req);

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Mutation_WithValidApiKey_ReturnsDispatchedExternalId()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            NotifyMutation,
            req => req.Headers.Add("X-Api-Key", AdminApiKey)
        );

        doc.RootElement.TryGetProperty("errors", out _).Should().BeFalse();
        var externalId = doc
            .RootElement.GetProperty("data")
            .GetProperty("dispatch")
            .GetProperty("audit")
            .GetProperty("notify")
            .GetProperty("externalId")
            .GetString();
        externalId.Should().NotBeNullOrEmpty();
    }

    // ── JWT scheme ───────────────────────────────────────────────────────

    [Test]
    public async Task Query_WithValidJwt_Succeeds()
    {
        using var host = await StartAsync(Schemes.Jwt);
        var token = SignJwt("alice", "Alice", "Player");

        using var doc = await host.PostGraphQLAsync(
            EchoQuery,
            req => req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token)
        );

        doc.RootElement.TryGetProperty("errors", out _).Should().BeFalse();
        var reply = doc
            .RootElement.GetProperty("data")
            .GetProperty("discover")
            .GetProperty("audit")
            .GetProperty("echo")
            .GetProperty("reply")
            .GetString();
        reply.Should().Be("echo: hi");
    }

    [Test]
    public async Task Query_WithInvalidJwt_OnProtectedEndpoint_Returns401()
    {
        using var host = await StartAsync(Schemes.Jwt);
        var client = host.GetTestServer().CreateClient();

        using var req = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql/protected")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { query = EchoQuery }),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.valid.jwt");

        var res = await client.SendAsync(req);

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Query_WithExpiredJwt_OnProtectedEndpoint_Returns401()
    {
        using var host = await StartAsync(Schemes.Jwt);
        var client = host.GetTestServer().CreateClient();

        // Forge an expired token using the real key so signature validates
        // but lifetime check fails.
        var key = new SymmetricSecurityKey(JwtKey);
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var now = DateTime.UtcNow;
        var expired = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: JwtIssuer,
            audience: JwtAudience,
            claims: [new System.Security.Claims.Claim("sub", "alice")],
            notBefore: now.AddHours(-2),
            expires: now.AddHours(-1),
            signingCredentials: creds
        );
        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(
            expired
        );

        using var req = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql/protected")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { query = EchoQuery }),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var res = await client.SendAsync(req);

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task Mutation_WithValidJwt_ReturnsDispatchedExternalId()
    {
        using var host = await StartAsync(Schemes.Jwt);
        var token = SignJwt("alice", "Alice", "Admin");

        using var doc = await host.PostGraphQLAsync(
            NotifyMutation,
            req => req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token)
        );

        doc.RootElement.TryGetProperty("errors", out _).Should().BeFalse();
        var externalId = doc
            .RootElement.GetProperty("data")
            .GetProperty("dispatch")
            .GetProperty("audit")
            .GetProperty("notify")
            .GetProperty("externalId")
            .GetString();
        externalId.Should().NotBeNullOrEmpty();
    }

    // ── Mixed-scheme coexistence ─────────────────────────────────────────

    [Test]
    public async Task BothSchemesRegistered_ApiKeyRequest_Succeeds()
    {
        using var host = await StartAsync(Schemes.ApiKey | Schemes.Jwt);

        using var doc = await host.PostGraphQLAsync(
            EchoQuery,
            req => req.Headers.Add("X-Api-Key", PlayerApiKey)
        );

        doc.RootElement.TryGetProperty("errors", out _).Should().BeFalse();
    }

    [Test]
    public async Task BothSchemesRegistered_JwtRequest_Succeeds()
    {
        using var host = await StartAsync(Schemes.ApiKey | Schemes.Jwt);
        var token = SignJwt("alice", "Alice", "Player");

        using var doc = await host.PostGraphQLAsync(
            EchoQuery,
            req => req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token)
        );

        doc.RootElement.TryGetProperty("errors", out _).Should().BeFalse();
    }

    [Test]
    public async Task BothSchemesRegistered_NoCredentialsOnProtectedEndpoint_Returns401()
    {
        using var host = await StartAsync(Schemes.ApiKey | Schemes.Jwt);
        var client = host.GetTestServer().CreateClient();

        using var req = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql/protected")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { query = EchoQuery }),
        };

        var res = await client.SendAsync(req);

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task AnonymousEndpoint_NoCredentials_Reaches200()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(EchoQuery);

        // Unprotected endpoint + train without [TraxAuthorize] ⇒ anonymous
        // access is deliberately allowed. Regression guard.
        doc.RootElement.TryGetProperty("errors", out _).Should().BeFalse();
    }
}
