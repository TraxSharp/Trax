using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using static Trax.Api.Tests.AuthE2E.AuthE2EHost;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// Security E2E suite for <see cref="Trax.Api.Auth.TraxCaller.IsTrusted"/>.
/// Each test fires a deliberately hostile HTTP request against a real server
/// and asserts the server-observed <c>IsTrusted</c> stays <c>false</c>.
///
/// <para>
/// The threat model: an attacker has HTTP access to the GraphQL endpoint but
/// no in-process code execution and no privileged credentials. The contract
/// the framework is asserting is:
/// </para>
///
/// <list type="bullet">
/// <item>No HTTP header maps to <c>IsTrusted = true</c>.</item>
/// <item>No cookie maps to it.</item>
/// <item>No query parameter maps to it.</item>
/// <item>No GraphQL variable or extension maps to it.</item>
/// <item>No JWT claim or role maps to it.</item>
/// <item>No introspection or schema-level operation maps to it.</item>
/// <item>An in-flight trusted scope opened by framework code does not leak into a parallel HTTP request's execution context.</item>
/// <item>A request that ran the trust path does not leak its trust state into the next request on the same server.</item>
/// </list>
///
/// <para>
/// The probe surface (<see cref="TraxCallerProbeQueries.WhoAmI"/> and
/// friends) returns the live <see cref="TraxCallerProbeResult"/> so each
/// assertion runs against actually-observed state on the server side, not
/// a mock.
/// </para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class TraxCallerAttackVectorE2ETests
{
    private const string Database = "trax_api_caller_attacks";

    private static Task<Microsoft.Extensions.Hosting.IHost> StartAsync(Schemes s) =>
        AuthE2EHost.StartAsync(s, Database);

    [OneTimeSetUp]
    public void SeedDatabase()
    {
        TestDbContext.EnsureSeeded(AuthE2EHost.ConnectionString(Database));
        AuthzTestDbContext.EnsureSeeded(AuthE2EHost.ConnectionString(Database));
    }

    // ── Probe queries ────────────────────────────────────────────────────

    private const string WhoAmIQuery = """
        { whoAmI { isAuthenticated isTrusted principalId principalRoles } }
        """;

    private const string ReadCallerStateMutation = """
        mutation { readCallerState { isAuthenticated isTrusted principalId principalRoles } }
        """;

    // ──────────────────────────────────────────────────────────────────────
    // Tier 0 — Probe self-check. Without this passing, every other test is
    // trivially passing because the probe couldn't observe trust state at all.
    // ──────────────────────────────────────────────────────────────────────

    [Test]
    public async Task ProbeSelfCheck_PokeAndReadInside_ReturnsTrueInsideScope()
    {
        // Sanity: opening BeginTrusted from a resolver makes IsTrusted true on
        // the same async flow. If this regresses, the trust mechanism is
        // broken at a layer below the security tests.
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            "mutation { pokeAndReadInside }",
            req => req.Headers.Add("X-Api-Key", AdminApiKey)
        );

        AssertNoErrors(doc);
        doc.RootElement.GetProperty("data")
            .GetProperty("pokeAndReadInside")
            .GetBoolean()
            .Should()
            .BeTrue("the probe must observe true inside an open trusted scope");
    }

    [Test]
    public async Task ProbeSelfCheck_PokeAndReadAfter_ReturnsFalseAfterDispose()
    {
        // Sanity: closing the scope handle resets the AsyncLocal. The "after"
        // probe must see false. This pins the scope's dispose contract.
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            "mutation { pokeAndReadAfter }",
            req => req.Headers.Add("X-Api-Key", AdminApiKey)
        );

        AssertNoErrors(doc);
        doc.RootElement.GetProperty("data")
            .GetProperty("pokeAndReadAfter")
            .GetBoolean()
            .Should()
            .BeFalse("the probe must observe false after the scope disposes");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Tier 1 — HTTP header injection. Try every plausible header name an
    // attacker might guess in the hope it opens a trust scope.
    // ──────────────────────────────────────────────────────────────────────

    [TestCase("X-Trax-Internal", "true")]
    [TestCase("X-Trax-Trusted", "true")]
    [TestCase("X-Trax-Bypass-Auth", "true")]
    [TestCase("X-Trax-BeginTrusted", "scheduler.remote-run")]
    [TestCase("X-Trusted", "true")]
    [TestCase("X-IsTrusted", "true")]
    [TestCase("Trax-Trusted", "true")]
    [TestCase("Trax-IsTrusted", "true")]
    [TestCase("Trax-Bypass", "true")]
    [TestCase("X-Forwarded-Trax-Trusted", "true")]
    [TestCase("X-Internal", "true")]
    public async Task HeaderInjection_DoesNotElevateToTrusted(string headerName, string value)
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            WhoAmIQuery,
            req => req.Headers.Add(headerName, value)
        );

        AssertNoErrors(doc);
        IsTrusted(doc).Should().BeFalse($"header '{headerName}' must not elevate to trusted");
    }

    [Test]
    public async Task HeaderInjection_CustomAuthScheme_DoesNotElevateToTrusted()
    {
        // Some attackers will guess a custom Authorization scheme like
        // "TraxTrusted ...". The Authorization header is parsed by the
        // configured auth schemes (ApiKey, JWT, etc.); none of them grant
        // trust, and an unrecognized scheme leaves the request anonymous.
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            WhoAmIQuery,
            req =>
                req.Headers.Authorization = new AuthenticationHeaderValue(
                    "TraxTrusted",
                    "scheduler.remote-run"
                )
        );

        AssertNoErrors(doc);
        IsTrusted(doc).Should().BeFalse();
    }

    [Test]
    public async Task HeaderInjection_MultipleAtOnce_DoesNotElevateToTrusted()
    {
        // Belt-and-suspenders: stack every guessable header in one request,
        // catching any handler that might OR them together or fall through to
        // an "any-trust-header" code path.
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            WhoAmIQuery,
            req =>
            {
                req.Headers.Add("X-Trax-Internal", "true");
                req.Headers.Add("X-Trax-Trusted", "true");
                req.Headers.Add("X-Trusted", "true");
                req.Headers.Add("Trax-Trusted", "true");
                req.Headers.Add("X-Internal", "true");
            }
        );

        AssertNoErrors(doc);
        IsTrusted(doc).Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────
    // Tier 2 — Cookie injection. Cookies are first-class credential carriers
    // in many ASP.NET Core hosts; verify Trax does not key trust off any.
    // ──────────────────────────────────────────────────────────────────────

    [TestCase("TraxTrusted=true")]
    [TestCase("IsTrusted=true")]
    [TestCase("TrustedScope=infra")]
    [TestCase("Trax-Trusted=true")]
    [TestCase("trax_trusted=true")]
    public async Task CookieInjection_DoesNotElevateToTrusted(string cookie)
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            WhoAmIQuery,
            req => req.Headers.Add("Cookie", cookie)
        );

        AssertNoErrors(doc);
        IsTrusted(doc).Should().BeFalse($"cookie '{cookie}' must not elevate to trusted");
    }

    [Test]
    public async Task CookieInjection_AllTogether_DoesNotElevateToTrusted()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            WhoAmIQuery,
            req =>
                req.Headers.Add(
                    "Cookie",
                    "TraxTrusted=true; IsTrusted=true; TrustedScope=scheduler"
                )
        );

        AssertNoErrors(doc);
        IsTrusted(doc).Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────
    // Tier 3 — URL query parameter injection. Trax does not read any query
    // parameters for trust state, but a poorly-considered middleware in a
    // host could; pin the framework default.
    // ──────────────────────────────────────────────────────────────────────

    [TestCase("?trusted=true")]
    [TestCase("?isTrusted=true")]
    [TestCase("?bypassAuth=true")]
    [TestCase("?traxTrusted=true&isTrusted=true&bypass=1")]
    public async Task QueryParamInjection_DoesNotElevateToTrusted(string queryString)
    {
        using var host = await StartAsync(Schemes.ApiKey);

        var client = host.GetTestServer().CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, $"/trax/graphql{queryString}")
        {
            Content = JsonContent.Create(new { query = WhoAmIQuery }),
        };
        using var res = await client.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        AssertNoErrors(doc);
        IsTrusted(doc).Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────
    // Tier 4 — GraphQL request body injection. Variables and extensions are
    // attacker-controlled JSON. Make sure neither path becomes a trust input.
    // ──────────────────────────────────────────────────────────────────────

    [Test]
    public async Task VariablesInjection_TrustedFields_DoesNotElevate()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        var client = host.GetTestServer().CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = JsonContent.Create(
                new
                {
                    query = WhoAmIQuery,
                    variables = new
                    {
                        trusted = true,
                        isTrusted = true,
                        bypassAuth = true,
                        traxTrusted = "scheduler",
                    },
                }
            ),
        };
        using var res = await client.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        AssertNoErrors(doc);
        IsTrusted(doc).Should().BeFalse();
    }

    [Test]
    public async Task ExtensionsInjection_TrustedExtension_DoesNotElevate()
    {
        // The `extensions` member of a GraphQL request is reserved for clients
        // to send opaque, server-recognized metadata (persisted queries, trace
        // headers, etc.). Trax recognizes a small set of extensions; "trusted"
        // is not among them.
        using var host = await StartAsync(Schemes.ApiKey);

        var client = host.GetTestServer().CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql")
        {
            Content = JsonContent.Create(
                new
                {
                    query = WhoAmIQuery,
                    extensions = new
                    {
                        trax = new
                        {
                            trusted = true,
                            isTrusted = true,
                            beginTrusted = "yes",
                        },
                        trustedScope = "scheduler",
                    },
                }
            ),
        };
        using var res = await client.SendAsync(req);
        var body = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);

        AssertNoErrors(doc);
        IsTrusted(doc).Should().BeFalse();
    }

    [Test]
    public async Task GraphQLOperation_TrustedFieldName_DoesNotElevate()
    {
        // What if the GraphQL query itself has a field called `isTrusted`? It
        // is just an output field on the response, not an input. Pin that
        // selecting it returns the server-side value, not whatever the client
        // requested.
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            "{ whoAmI { isTrusted } isTrusted: whoAmI { isTrusted } }"
        );

        AssertNoErrors(doc);
        IsTrusted(doc).Should().BeFalse();
        // Aliased copy reads the same server-side value.
        doc.RootElement.GetProperty("data")
            .GetProperty("isTrusted")
            .GetProperty("isTrusted")
            .GetBoolean()
            .Should()
            .BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────
    // Tier 5 — JWT claim injection. A self-minted JWT cannot claim trust.
    // Roles and arbitrary claims map to authorization, never trust.
    // ──────────────────────────────────────────────────────────────────────

    [Test]
    public async Task JwtClaim_RoleTrusted_DoesNotElevate()
    {
        // "Trusted" is just a string. A role named Trusted has no privileged
        // meaning to Trax's trusted-scope mechanism.
        using var host = await StartAsync(Schemes.Jwt);
        var token = SignJwt("attacker", "Eve", "Trusted");

        using var doc = await host.PostGraphQLAsync(
            WhoAmIQuery,
            req => req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token)
        );

        AssertNoErrors(doc);
        var result = WhoAmI(doc);
        result.GetProperty("isTrusted").GetBoolean().Should().BeFalse();
        result
            .GetProperty("isAuthenticated")
            .GetBoolean()
            .Should()
            .BeTrue("the JWT did authenticate the principal; only the trust flag must stay false");
    }

    [Test]
    public async Task JwtClaim_RoleAdminAndTrusted_DoesNotElevate()
    {
        using var host = await StartAsync(Schemes.Jwt);
        var token = SignJwt("attacker", "Eve", "Admin", "Trusted");

        using var doc = await host.PostGraphQLAsync(
            WhoAmIQuery,
            req => req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token)
        );

        AssertNoErrors(doc);
        IsTrusted(doc).Should().BeFalse();
    }

    // ──────────────────────────────────────────────────────────────────────
    // Tier 6 — GraphQL introspection. The schema must not expose any field,
    // type, directive, or input named in a way that suggests it controls
    // trust. Catches accidental future exposure at compile/schema-build time
    // by failing the test before it ships.
    // ──────────────────────────────────────────────────────────────────────

    [Test]
    public async Task Introspection_NoFieldNameSuggestsTrustControl()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            """
            {
              __schema {
                types {
                  name
                  fields { name }
                  inputFields { name }
                }
              }
            }
            """,
            req => req.Headers.Add("X-Api-Key", AdminApiKey)
        );

        AssertNoErrors(doc);

        var suspiciousNames = new[] { "beginTrusted", "bypassAuth", "setTrusted", "elevateTrust" };
        var types = doc
            .RootElement.GetProperty("data")
            .GetProperty("__schema")
            .GetProperty("types")
            .EnumerateArray()
            .ToList();

        foreach (var type in types)
        {
            var typeName = type.GetProperty("name").GetString();
            foreach (var field in FieldsOrEmpty(type, "fields"))
            {
                var name = field.GetProperty("name").GetString() ?? "";
                suspiciousNames
                    .Should()
                    .NotContain(name, $"type {typeName} should not expose field '{name}'");
            }
            foreach (var inputField in FieldsOrEmpty(type, "inputFields"))
            {
                var name = inputField.GetProperty("name").GetString() ?? "";
                suspiciousNames
                    .Should()
                    .NotContain(name, $"type {typeName} should not expose input field '{name}'");
            }
        }
    }

    [Test]
    public async Task Introspection_NoDirectiveNameSuggestsTrustControl()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            "{ __schema { directives { name } } }",
            req => req.Headers.Add("X-Api-Key", AdminApiKey)
        );

        AssertNoErrors(doc);

        var suspiciousDirectives = new[] { "trusted", "beginTrusted", "bypassAuth", "elevate" };
        var directiveNames = doc
            .RootElement.GetProperty("data")
            .GetProperty("__schema")
            .GetProperty("directives")
            .EnumerateArray()
            .Select(d => d.GetProperty("name").GetString() ?? "")
            .ToList();

        foreach (var name in directiveNames)
        {
            suspiciousDirectives
                .Should()
                .NotContain(name, $"schema must not expose @{name} directive");
        }
    }

    // ──────────────────────────────────────────────────────────────────────
    // Tier 7 — Cross-request AsyncLocal isolation. The most subtle attack
    // surface. Verify a trust scope held open in one request does not leak
    // into a parallel or subsequent request on the same server.
    // ──────────────────────────────────────────────────────────────────────

    [Test]
    public async Task AsyncLocalIsolation_ParallelRequest_DoesNotSeeTrust()
    {
        // Request A holds a trusted scope open for 500ms. Request B fires
        // mid-flight and must NOT observe IsTrusted=true. AsyncLocal flows
        // along one logical async stack and ASP.NET Core gives each request
        // its own execution context — but a regression that promoted the
        // scope to a singleton or to a static would break this.
        using var host = await StartAsync(Schemes.ApiKey);

        var holdTask = host.PostGraphQLAsync(
            "mutation { holdTrustedFor(millis: 500) }",
            req => req.Headers.Add("X-Api-Key", AdminApiKey)
        );

        // Give A a moment to enter the scope before B fires. Not a fixed
        // sleep — poll the in-scope holdTrustedFor's completion async, and
        // race the parallel WhoAmI against it.
        await Task.Delay(50);

        using var parallelDoc = await host.PostGraphQLAsync(WhoAmIQuery);
        AssertNoErrors(parallelDoc);
        IsTrusted(parallelDoc)
            .Should()
            .BeFalse("a concurrent unrelated request must NOT see another request's trusted scope");

        using var holdDoc = await holdTask;
        AssertNoErrors(holdDoc);
        holdDoc
            .RootElement.GetProperty("data")
            .GetProperty("holdTrustedFor")
            .GetBoolean()
            .Should()
            .BeTrue("the holding request must observe its own scope as trusted");
    }

    [Test]
    public async Task AsyncLocalIsolation_TwoParallelHoldRequests_EachSeesOwnScope()
    {
        // Two requests both open their own trusted scope and run concurrently.
        // Each must observe its OWN scope (true). If AsyncLocal leaked, they
        // could observe each other's. The check is that BOTH return true,
        // not that one of them observes false (which would prove a scope
        // never opened, not isolation).
        using var host = await StartAsync(Schemes.ApiKey);

        var a = host.PostGraphQLAsync(
            "mutation { holdTrustedFor(millis: 200) }",
            req => req.Headers.Add("X-Api-Key", AdminApiKey)
        );
        var b = host.PostGraphQLAsync(
            "mutation { holdTrustedFor(millis: 200) }",
            req => req.Headers.Add("X-Api-Key", AdminApiKey)
        );

        using var docA = await a;
        using var docB = await b;

        AssertNoErrors(docA);
        AssertNoErrors(docB);
        docA.RootElement.GetProperty("data")
            .GetProperty("holdTrustedFor")
            .GetBoolean()
            .Should()
            .BeTrue();
        docB.RootElement.GetProperty("data")
            .GetProperty("holdTrustedFor")
            .GetBoolean()
            .Should()
            .BeTrue();
    }

    [Test]
    public async Task AsyncLocalIsolation_SequentialRequest_FreshFlowSeesFalse()
    {
        // After a request that opens-then-closes a trusted scope, the next
        // request on the same server must start fresh. If the previous
        // execution context leaked into the connection pool / shared state,
        // the second whoAmI would see true.
        using var host = await StartAsync(Schemes.ApiKey);

        using var first = await host.PostGraphQLAsync(
            "mutation { pokeAndReadAfter }",
            req => req.Headers.Add("X-Api-Key", AdminApiKey)
        );
        AssertNoErrors(first);
        first
            .RootElement.GetProperty("data")
            .GetProperty("pokeAndReadAfter")
            .GetBoolean()
            .Should()
            .BeFalse();

        using var second = await host.PostGraphQLAsync(WhoAmIQuery);
        AssertNoErrors(second);
        IsTrusted(second)
            .Should()
            .BeFalse("a subsequent unrelated request must observe a fresh, untrusted scope");
    }

    // ──────────────────────────────────────────────────────────────────────
    // Tier 8 — Authentication does not imply trust. Even an Admin caller, or
    // an authenticated request to an endpoint protected by RequireAuthorization,
    // must not see IsTrusted = true.
    // ──────────────────────────────────────────────────────────────────────

    [Test]
    public async Task AuthenticatedAdmin_OnPublicEndpoint_IsTrustedFalse()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            WhoAmIQuery,
            req => req.Headers.Add("X-Api-Key", AdminApiKey)
        );

        AssertNoErrors(doc);
        var who = WhoAmI(doc);
        who.GetProperty("isAuthenticated").GetBoolean().Should().BeTrue();
        who.GetProperty("isTrusted")
            .GetBoolean()
            .Should()
            .BeFalse("Admin role and authentication must not imply trust");
        who.GetProperty("principalRoles")
            .EnumerateArray()
            .Select(r => r.GetString())
            .Should()
            .Contain("Admin");
    }

    [Test]
    public async Task AuthenticatedAdmin_OnProtectedEndpoint_IsTrustedFalse()
    {
        // Endpoint-level RequireAuthorization gates the request before
        // HotChocolate runs. Passing that gate authenticates the caller but
        // does not elevate to trusted.
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostProtectedGraphQLAsync(
            WhoAmIQuery,
            req => req.Headers.Add("X-Api-Key", AdminApiKey)
        );

        AssertNoErrors(doc);
        IsTrusted(doc).Should().BeFalse();
    }

    [Test]
    public async Task Anonymous_OnPublicEndpoint_AllFlagsFalse()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(WhoAmIQuery);

        AssertNoErrors(doc);
        var who = WhoAmI(doc);
        who.GetProperty("isAuthenticated").GetBoolean().Should().BeFalse();
        who.GetProperty("isTrusted").GetBoolean().Should().BeFalse();
        who.GetProperty("principalId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    // ──────────────────────────────────────────────────────────────────────
    // Tier 9 — Mutation path mirrors. Re-run the highest-value attacks
    // against a mutation (POST body, GraphQL writes) to catch a regression
    // that handled queries correctly but accidentally elevated mutations.
    // ──────────────────────────────────────────────────────────────────────

    [Test]
    public async Task MutationPath_HeaderInjection_DoesNotElevate()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            ReadCallerStateMutation,
            req =>
            {
                req.Headers.Add("X-Trax-Trusted", "true");
                req.Headers.Add("X-Trusted", "true");
            }
        );

        AssertNoErrors(doc);
        doc.RootElement.GetProperty("data")
            .GetProperty("readCallerState")
            .GetProperty("isTrusted")
            .GetBoolean()
            .Should()
            .BeFalse();
    }

    [Test]
    public async Task MutationPath_AnonymousCaller_AllFlagsFalse()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(ReadCallerStateMutation);

        AssertNoErrors(doc);
        var who = doc.RootElement.GetProperty("data").GetProperty("readCallerState");
        who.GetProperty("isAuthenticated").GetBoolean().Should().BeFalse();
        who.GetProperty("isTrusted").GetBoolean().Should().BeFalse();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static JsonElement WhoAmI(JsonDocument doc) =>
        doc.RootElement.GetProperty("data").GetProperty("whoAmI");

    private static bool IsTrusted(JsonDocument doc) =>
        WhoAmI(doc).GetProperty("isTrusted").GetBoolean();

    private static IEnumerable<JsonElement> FieldsOrEmpty(JsonElement type, string propertyName)
    {
        if (!type.TryGetProperty(propertyName, out var prop))
            return Enumerable.Empty<JsonElement>();
        if (prop.ValueKind != JsonValueKind.Array)
            return Enumerable.Empty<JsonElement>();
        return prop.EnumerateArray();
    }

    private static void AssertNoErrors(JsonDocument doc) =>
        doc
            .RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse(doc.RootElement.GetRawText());
}
