using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using static Trax.Api.Tests.AuthE2E.AuthE2EHost;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// End-to-end coverage for per-train <c>[TraxAuthorize]</c> enforcement
/// against the real Trax GraphQL endpoint. Verifies that the full chain
/// (credential → principal → ClaimsPrincipal → <c>TrainAuthorizationService</c>
/// → train execution) rejects unauthorized callers with the opaque
/// <c>TRAX_AUTHORIZATION</c> error, and admits authorized callers.
///
/// The admin-gated trains live next to the unprotected trains in
/// <see cref="AuthE2EHost"/>'s shared assembly, so the mediator registers
/// all of them together.
/// </summary>
[TestFixture]
[NonParallelizable]
public class TraxAuthorizeE2ETests
{
    private const string AdminLookupQuery = """
        query AdminLookup {
          discover { admin { adminLookup(input: { target: "x" }) { secret } } }
        }
        """;

    private const string WipeMutation = """
        mutation Wipe {
          dispatch { admin { wipe(input: { target: "x" }) { externalId } } }
        }
        """;

    // ── [TraxAuthorize(Roles="Admin")] on a query train ──────────────────

    [Test]
    public async Task AdminQuery_WithAdminApiKey_Succeeds()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            AdminLookupQuery,
            req => req.Headers.Add("X-Api-Key", AdminApiKey)
        );

        doc.RootElement.TryGetProperty("errors", out _).Should().BeFalse();
        var secret = doc
            .RootElement.GetProperty("data")
            .GetProperty("discover")
            .GetProperty("admin")
            .GetProperty("adminLookup")
            .GetProperty("secret")
            .GetString();
        secret.Should().Be("classified:x");
    }

    [Test]
    public async Task AdminQuery_WithPlayerApiKey_Returns_TRAX_AUTHORIZATION()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            AdminLookupQuery,
            req => req.Headers.Add("X-Api-Key", PlayerApiKey)
        );

        AssertTraxAuthorizationError(doc);
    }

    [Test]
    public async Task AdminQuery_AnonymousNoCredentials_Returns_TRAX_AUTHORIZATION()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(AdminLookupQuery);

        AssertTraxAuthorizationError(doc);
    }

    [Test]
    public async Task AdminQuery_WithAdminJwt_Succeeds()
    {
        using var host = await StartAsync(Schemes.Jwt);
        var token = SignJwt("alice", "Alice", "Admin");

        using var doc = await host.PostGraphQLAsync(
            AdminLookupQuery,
            req => req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token)
        );

        doc.RootElement.TryGetProperty("errors", out _).Should().BeFalse();
        var secret = doc
            .RootElement.GetProperty("data")
            .GetProperty("discover")
            .GetProperty("admin")
            .GetProperty("adminLookup")
            .GetProperty("secret")
            .GetString();
        secret.Should().Be("classified:x");
    }

    [Test]
    public async Task AdminQuery_WithPlayerOnlyJwt_Returns_TRAX_AUTHORIZATION()
    {
        using var host = await StartAsync(Schemes.Jwt);
        var token = SignJwt("alice", "Alice", "Player");

        using var doc = await host.PostGraphQLAsync(
            AdminLookupQuery,
            req => req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token)
        );

        AssertTraxAuthorizationError(doc);
    }

    [Test]
    public async Task AdminQuery_WithNoRolesJwt_Returns_TRAX_AUTHORIZATION()
    {
        using var host = await StartAsync(Schemes.Jwt);
        var token = SignJwt("alice", "Alice");

        using var doc = await host.PostGraphQLAsync(
            AdminLookupQuery,
            req => req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token)
        );

        AssertTraxAuthorizationError(doc);
    }

    // ── [TraxAuthorize(Policy="AdminPolicy")] on a mutation train ───────

    [Test]
    public async Task WipeMutation_WithAdminApiKey_Succeeds()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            WipeMutation,
            req => req.Headers.Add("X-Api-Key", AdminApiKey)
        );

        doc.RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse(doc.RootElement.GetRawText());
        // Response object exists and isn't null on success.
        doc.RootElement.GetProperty("data")
            .GetProperty("dispatch")
            .GetProperty("admin")
            .GetProperty("wipe")
            .ValueKind.Should()
            .NotBe(JsonValueKind.Null);
    }

    [Test]
    public async Task WipeMutation_WithPlayerApiKey_Fails()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            WipeMutation,
            req => req.Headers.Add("X-Api-Key", PlayerApiKey)
        );

        // AdminPolicy is an ASP.NET Core policy, not a TraxAuthorize-role
        // check. Trax surfaces the policy failure as a generic TRAX_AUTHORIZATION
        // error, but the inner "Not authorized." wording may differ slightly
        // depending on pipeline state. Verify at minimum that the mutation
        // did NOT succeed.
        doc.RootElement.TryGetProperty("errors", out var errors).Should().BeTrue();
        errors.GetArrayLength().Should().BeGreaterThan(0);
    }

    [Test]
    public async Task WipeMutation_WithAdminJwt_Succeeds()
    {
        using var host = await StartAsync(Schemes.Jwt);
        var token = SignJwt("alice", "Alice", "Admin");

        using var doc = await host.PostGraphQLAsync(
            WipeMutation,
            req => req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token)
        );

        doc.RootElement.TryGetProperty("errors", out _).Should().BeFalse();
    }

    // ── Mixed-scheme coexistence against gated trains ────────────────────
    //
    // When multiple auth schemes are registered, no single scheme is implicit
    // default; ASP.NET Core requires either endpoint-level authorization or
    // an explicit default scheme to authenticate a request. These tests hit
    // the /trax/graphql/protected endpoint which gates on TraxAuthPolicy —
    // that policy runs authentication for every registered Trax scheme and
    // admits any that succeeds.

    [Test]
    public async Task BothSchemes_ApiKeyAdmin_SucceedsAgainstAdminTrain()
    {
        using var host = await StartAsync(Schemes.ApiKey | Schemes.Jwt);

        using var doc = await host.PostProtectedGraphQLAsync(
            AdminLookupQuery,
            req => req.Headers.Add("X-Api-Key", AdminApiKey)
        );

        doc.RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse(doc.RootElement.GetRawText());
    }

    [Test]
    public async Task BothSchemes_JwtAdmin_SucceedsAgainstAdminTrain()
    {
        using var host = await StartAsync(Schemes.ApiKey | Schemes.Jwt);
        var token = SignJwt("alice", "Alice", "Admin");

        using var doc = await host.PostProtectedGraphQLAsync(
            AdminLookupQuery,
            req => req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token)
        );

        doc.RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse(doc.RootElement.GetRawText());
    }

    [Test]
    public async Task BothSchemes_JwtPlayerOnly_FailsAgainstAdminTrain()
    {
        using var host = await StartAsync(Schemes.ApiKey | Schemes.Jwt);
        var token = SignJwt("alice", "Alice", "Player");

        using var doc = await host.PostProtectedGraphQLAsync(
            AdminLookupQuery,
            req => req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token)
        );

        AssertTraxAuthorizationError(doc);
    }

    // ── Error shape / opacity guard ──────────────────────────────────────

    [Test]
    public async Task ErrorMessage_DoesNotLeakTrainName_Or_RequiredRole()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            AdminLookupQuery,
            req => req.Headers.Add("X-Api-Key", PlayerApiKey)
        );

        var raw = doc.RootElement.GetRawText();
        raw.Should().NotContain("AdminLookupTrain");
        raw.Should().NotContain("IAdminLookupTrain");
        raw.Should().NotContain("Admin", "role name should not leak in the error body");
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static void AssertTraxAuthorizationError(JsonDocument doc)
    {
        doc.RootElement.TryGetProperty("errors", out var errors).Should().BeTrue();
        errors.GetArrayLength().Should().BeGreaterThan(0);

        var first = errors[0];
        first.GetProperty("message").GetString().Should().Be("Not authorized.");
        first
            .GetProperty("extensions")
            .GetProperty("code")
            .GetString()
            .Should()
            .Be("TRAX_AUTHORIZATION");
    }
}
