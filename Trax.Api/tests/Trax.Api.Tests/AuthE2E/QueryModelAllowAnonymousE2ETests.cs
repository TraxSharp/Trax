using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Trax.Api.Auth;
using static Trax.Api.Tests.AuthE2E.AuthE2EHost;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// End-to-end coverage for <c>[TraxAllowAnonymous]</c> on <c>[TraxQueryModel]</c>
/// entities. The attribute opens the directly decorated entity to anonymous
/// callers while leaving gated children to enforce their own <c>@authorize</c>
/// directive when reached transitively (Option B: no cascade).
///
/// <para>
/// Fixture composition is intentional. <see cref="PublicBook"/> is anonymous,
/// <see cref="OwnedBook"/> is <c>[TraxAuthorize(Roles = "Admin")]</c>, and
/// <see cref="Owner"/> is ungated. The fixture seeds PublicBooks owned by
/// both Alice and Bob, with at least one PublicBook linked to a gated
/// OwnedBook so the cascade-from-anonymous path actually attempts to
/// materialise a gated child.
/// </para>
/// </summary>
[TestFixture]
[NonParallelizable]
public class QueryModelAllowAnonymousE2ETests
{
    private const string Database = "trax_api_auth_allow_anonymous";

    private static Task<Microsoft.Extensions.Hosting.IHost> StartAsync(Schemes s) =>
        AuthE2EHost.StartAsync(s, Database);

    [OneTimeSetUp]
    public void SeedDatabase()
    {
        // Both contexts use the same DB; seed the unauthorized fixtures too so
        // any shared startup paths that touch test_auth do not blow up.
        TestDbContext.EnsureSeeded(AuthE2EHost.ConnectionString(Database));
        AuthzTestDbContext.EnsureSeeded(AuthE2EHost.ConnectionString(Database));
    }

    // ── Queries ──────────────────────────────────────────────────────────

    private const string DirectPublicBooksQuery = """
        { discover { vault { publicBooks { totalCount nodes { title } } } } }
        """;

    private const string PublicBooksWithCascadeQuery = """
        {
          discover {
            vault {
              publicBooks {
                nodes {
                  title
                  linkedOwnedBook { title }
                }
              }
            }
          }
        }
        """;

    private const string OwnersWithPublicBooksQuery = """
        {
          discover {
            vault {
              owners {
                nodes {
                  name
                  publicBooks { title }
                }
              }
            }
          }
        }
        """;

    private const string OwnedBooksQuery = """
        { discover { vault { ownedBooks { totalCount } } } }
        """;

    // ── Direct AllowAnonymous access ─────────────────────────────────────

    [Test]
    public async Task DirectPublicBooks_Anonymous_Succeeds()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(DirectPublicBooksQuery);

        AssertNoErrors(doc);
        PublicBooksField(doc).GetProperty("totalCount").GetInt32().Should().Be(3);
    }

    [Test]
    public async Task DirectPublicBooks_AsPlayer_Succeeds()
    {
        // AllowAnonymous does not exclude authenticated callers; it just
        // removes the gate. A Player must still see the same rows an anonymous
        // caller sees.
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            DirectPublicBooksQuery,
            req => req.Headers.Add("X-Api-Key", PlayerApiKey)
        );

        AssertNoErrors(doc);
        PublicBooksField(doc).GetProperty("totalCount").GetInt32().Should().Be(3);
    }

    [Test]
    public async Task DirectPublicBooks_AsAdmin_Succeeds()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            DirectPublicBooksQuery,
            req => req.Headers.Add("X-Api-Key", AdminApiKey)
        );

        AssertNoErrors(doc);
        PublicBooksField(doc).GetProperty("totalCount").GetInt32().Should().Be(3);
    }

    // ── Connection scalars reachable anonymously ─────────────────────────

    [Test]
    public async Task DirectPublicBooks_TotalCountOnly_Anonymous_Succeeds()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            "{ discover { vault { publicBooks { totalCount } } } }"
        );

        AssertNoErrors(doc);
        PublicBooksField(doc).GetProperty("totalCount").GetInt32().Should().Be(3);
    }

    [Test]
    public async Task DirectPublicBooks_PageInfoOnly_Anonymous_Succeeds()
    {
        // Pinning that field-level @authorize is not silently attached. A
        // regression that emitted the directive at field level only would
        // pass the type-level test (the body wouldn't include node fields)
        // but fail this one.
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            "{ discover { vault { publicBooks { pageInfo { hasNextPage } } } } }"
        );

        AssertNoErrors(doc);
        PublicBooksField(doc)
            .GetProperty("pageInfo")
            .GetProperty("hasNextPage")
            .ValueKind.Should()
            .Be(JsonValueKind.False);
    }

    [Test]
    public async Task DirectPublicBooks_EdgesCursorOnly_Anonymous_Succeeds()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            "{ discover { vault { publicBooks(first: 2) { edges { cursor } } } } }"
        );

        AssertNoErrors(doc);
        PublicBooksField(doc).GetProperty("edges").GetArrayLength().Should().Be(2);
    }

    // ── CRITICAL: cascade-from-anonymous-to-gated (Option B) ─────────────

    [Test]
    public async Task TransitivePublicToOwnedBooks_Anonymous_BlockedOnChild()
    {
        // PublicBook is anonymous. OwnedBook is Admin-gated. Reaching
        // OwnedBook through PublicBook.linkedOwnedBook must still fire the
        // child's @authorize directive. This is the Option B contract: the
        // anonymous parent does not propagate openness to its gated children.
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(PublicBooksWithCascadeQuery);

        AssertTraxAuthorizationError(doc);
    }

    [Test]
    public async Task TransitivePublicToOwnedBooks_AsPlayer_BlockedOnChild()
    {
        // A Player lacks the Admin role, so the cascade still rejects even
        // for an authenticated principal. The gate is the child's role
        // requirement, not the parent's anonymous flag.
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            PublicBooksWithCascadeQuery,
            req => req.Headers.Add("X-Api-Key", PlayerApiKey)
        );

        AssertTraxAuthorizationError(doc);
    }

    [Test]
    public async Task TransitivePublicToOwnedBooks_AsAdmin_Succeeds()
    {
        // Admin satisfies the child gate, so the full cascade resolves. The
        // anonymous parent didn't block them — proving the AllowAnonymous
        // flag is purely permissive on the entity it decorates.
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            PublicBooksWithCascadeQuery,
            req => req.Headers.Add("X-Api-Key", AdminApiKey)
        );

        AssertNoErrors(doc);
        var nodes = doc
            .RootElement.GetProperty("data")
            .GetProperty("discover")
            .GetProperty("vault")
            .GetProperty("publicBooks")
            .GetProperty("nodes")
            .EnumerateArray()
            .ToList();
        nodes.Should().HaveCount(3);
        nodes
            .Select(n => n.GetProperty("title").GetString())
            .Should()
            .Contain("Alice's Public Notice");
    }

    [Test]
    public async Task TransitivePublicToOwnedBooks_Anonymous_DoesNotLeakOwnedBookTitles()
    {
        // Payload-leak guard: no OwnedBook title from the seed may appear
        // anywhere in the anonymous response — not in data, not in errors,
        // not in extensions.
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(PublicBooksWithCascadeQuery);

        var raw = doc.RootElement.GetRawText();
        raw.Should().NotContain("Alice's First Book");
        raw.Should().NotContain("Alice's Second Book");
        raw.Should().NotContain("Bob's Only Book");
    }

    // ── Anonymous traversal through ungated parent → anonymous target ────

    [Test]
    public async Task OwnerToPublicBooks_Anonymous_Succeeds()
    {
        // The reverse traversal: from ungated Owner to anonymous PublicBook.
        // Both sides allow anonymous access, so the request must succeed and
        // return data. This is the symmetric case to the cascade test —
        // proving the AllowAnonymous flag actually opens the type for
        // navigation, not just direct queries.
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(OwnersWithPublicBooksQuery);

        AssertNoErrors(doc);
        var owners = doc
            .RootElement.GetProperty("data")
            .GetProperty("discover")
            .GetProperty("vault")
            .GetProperty("owners")
            .GetProperty("nodes")
            .EnumerateArray()
            .ToList();
        owners.Should().HaveCount(2);
        var alice = owners.Single(o => o.GetProperty("name").GetString() == "Alice");
        alice.GetProperty("publicBooks").GetArrayLength().Should().Be(2);
    }

    // ── Regression: pre-existing gates still hold ────────────────────────

    [Test]
    public async Task Regression_OwnedBooks_Anonymous_StillBlocked()
    {
        // Adding AllowAnonymous to one entity must not weaken the gate on
        // another. The pre-existing OwnedBook (Admin-gated) test runs in the
        // same host wiring; pin the regression here to fail loudly if a
        // refactor confused the two flags.
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(OwnedBooksQuery);

        AssertTraxAuthorizationError(doc);
    }

    [Test]
    public async Task Regression_MemberAreas_Anonymous_StillBlocked()
    {
        // Bare [TraxAuthorize] (any-authenticated) on a sibling entity must
        // still deny anonymous callers when an unrelated sibling carries
        // AllowAnonymous.
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            "{ discover { vault { memberAreas { totalCount } } } }"
        );

        AssertTraxAuthorizationError(doc);
    }

    // ── TraxPrincipal availability contract under AllowAnonymous flows ───

    [Test]
    public async Task Anonymous_DiResolutionOfTraxPrincipal_ThrowsNotAvailable()
    {
        // Pins the existing fail-loud contract: TraxPrincipal is a scoped
        // service that throws TraxPrincipalNotAvailableException when no
        // authenticated user is present on the current scope. Code that runs
        // in both anonymous and authenticated paths (junctions reused across
        // gated and AllowAnonymous trains, future row-level filters, etc.)
        // must NOT inject TraxPrincipal directly — it has to inject
        // IHttpContextAccessor and call TryGetTraxPrincipal.
        //
        // This test is the canary: if a future change relaxes the throw to
        // return a sentinel principal, this assertion fails and the change
        // surfaces in code review with the full implication ("you just
        // changed the public contract of TraxPrincipal injection") visible.
        using var host = await StartAsync(Schemes.ApiKey);
        using var scope = host.Services.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<TraxPrincipal>();

        act.Should()
            .Throw<TraxPrincipalNotAvailableException>(
                "anonymous scopes have no authenticated principal; injection must throw"
            );
    }

    [Test]
    public async Task AnonymousRequest_HttpUser_HasNoTraxPrincipalClaim()
    {
        // The complementary safe path: code that needs to handle both
        // anonymous and authenticated flows uses
        // ClaimsPrincipal.TryGetTraxPrincipal(out var p) and gets `false` for
        // anonymous scopes. Pin that the anonymous flow really does return
        // false rather than constructing a partial principal — same canary
        // as above, applied to the safe API surface.
        using var host = await StartAsync(Schemes.ApiKey);
        using var scope = host.Services.CreateScope();

        var anonymousUser = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity()
        );
        var ok = anonymousUser.TryGetTraxPrincipal(out var principal);

        ok.Should().BeFalse("an unauthenticated ClaimsPrincipal carries no Trax claim");
        principal.Should().BeNull();
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static JsonElement PublicBooksField(JsonDocument doc) => VaultField(doc, "publicBooks");

    private static JsonElement VaultField(JsonDocument doc, string fieldName) =>
        doc
            .RootElement.GetProperty("data")
            .GetProperty("discover")
            .GetProperty("vault")
            .GetProperty(fieldName);

    private static void AssertNoErrors(JsonDocument doc) =>
        doc
            .RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse(doc.RootElement.GetRawText());

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
