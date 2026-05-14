using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using static Trax.Api.Tests.AuthE2E.AuthE2EHost;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// End-to-end coverage for <c>[TraxAuthorize]</c> applied to
/// <c>[TraxQueryModel]</c>-annotated entities. The attribute attaches the
/// <c>@authorize</c> directive to the generated <c>ObjectType</c> so the
/// gate enforces uniformly regardless of how the type is reached:
///
/// <list type="bullet">
/// <item>top-level <c>discover.vault.ownedBooks</c> (direct entry point)</item>
/// <item>transitively via <c>discover.vault.owners[].books</c> (the navigation
/// path through an ungated parent)</item>
/// </list>
///
/// The transitive case is the security-critical contract: a Player must not be
/// able to read <see cref="OwnedBook"/> rows by traversing into them from
/// <see cref="Owner"/>, which is itself ungated.
/// </summary>
[TestFixture]
[NonParallelizable]
public class QueryModelAuthorizeE2ETests
{
    private const string Database = "trax_api_auth_querymodel";

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

    // ── Queries ───────────────────────────────────────────────────────────

    private const string DirectOwnedBooksQuery = """
        { discover { vault { ownedBooks { totalCount nodes { title } } } } }
        """;

    private const string OwnersOnlyQuery = """
        { discover { vault { owners { totalCount nodes { name } } } } }
        """;

    /// <summary>
    /// The transitive-navigation probe. Requests OwnedBook through the Owner's
    /// books navigation. Must fail for anyone lacking the OwnedBook gate.
    /// </summary>
    private const string OwnersWithBooksQuery = """
        {
          discover {
            vault {
              owners {
                nodes { name books { title } }
              }
            }
          }
        }
        """;

    private const string MemosQuery = """
        { discover { vault { memos { totalCount } } } }
        """;

    private const string RestrictedDocsQuery = """
        { discover { vault { restrictedDocs { totalCount } } } }
        """;

    private const string MemberAreasQuery = """
        { discover { vault { memberAreas { totalCount } } } }
        """;

    // ── Top-level gating: Roles="Admin" ──────────────────────────────────

    [Test]
    public async Task DirectOwnedBooks_AsAdmin_Succeeds()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            DirectOwnedBooksQuery,
            req => req.Headers.Add("X-Api-Key", AdminApiKey)
        );

        AssertNoErrors(doc);
        OwnedBooksField(doc).GetProperty("totalCount").GetInt32().Should().Be(3);
    }

    [Test]
    public async Task DirectOwnedBooks_AsPlayer_ReturnsAuthorizationError()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            DirectOwnedBooksQuery,
            req => req.Headers.Add("X-Api-Key", PlayerApiKey)
        );

        AssertTraxAuthorizationError(doc);
    }

    [Test]
    public async Task DirectOwnedBooks_Anonymous_ReturnsAuthorizationError()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(DirectOwnedBooksQuery);

        AssertTraxAuthorizationError(doc);
    }

    // ── Ungated sibling remains reachable ────────────────────────────────

    [Test]
    public async Task UngatedOwners_AsPlayer_Succeeds()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            OwnersOnlyQuery,
            req => req.Headers.Add("X-Api-Key", PlayerApiKey)
        );

        AssertNoErrors(doc);
        doc.RootElement.GetProperty("data")
            .GetProperty("discover")
            .GetProperty("vault")
            .GetProperty("owners")
            .GetProperty("totalCount")
            .GetInt32()
            .Should()
            .Be(2);
    }

    [Test]
    public async Task UngatedOwners_Anonymous_Succeeds()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(OwnersOnlyQuery);

        AssertNoErrors(doc);
        doc.RootElement.GetProperty("data")
            .GetProperty("discover")
            .GetProperty("vault")
            .GetProperty("owners")
            .GetProperty("totalCount")
            .GetInt32()
            .Should()
            .Be(2);
    }

    // ── CRITICAL: transitive navigation enforcement ──────────────────────

    [Test]
    public async Task TransitiveBooks_AsPlayer_FailsAuthorizationOnChildren()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            OwnersWithBooksQuery,
            req => req.Headers.Add("X-Api-Key", PlayerApiKey)
        );

        AssertTraxAuthorizationError(doc);
    }

    [Test]
    public async Task TransitiveBooks_Anonymous_FailsAuthorizationOnChildren()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(OwnersWithBooksQuery);

        AssertTraxAuthorizationError(doc);
    }

    [Test]
    public async Task TransitiveBooks_AsAdmin_Succeeds()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            OwnersWithBooksQuery,
            req => req.Headers.Add("X-Api-Key", AdminApiKey)
        );

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
        alice.GetProperty("books").GetArrayLength().Should().Be(2);
    }

    [Test]
    public async Task TransitiveBooks_AsPlayer_DoesNotLeakBookTitlesInPayload()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            OwnersWithBooksQuery,
            req => req.Headers.Add("X-Api-Key", PlayerApiKey)
        );

        // No book title from the seed fixture may appear anywhere in the
        // response body — not in data, not in errors, not in extensions.
        var raw = doc.RootElement.GetRawText();
        raw.Should().NotContain("Alice's First Book");
        raw.Should().NotContain("Alice's Second Book");
        raw.Should().NotContain("Bob's Only Book");
    }

    [Test]
    public async Task TransitiveBooks_Anonymous_DoesNotLeakBookTitlesInPayload()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(OwnersWithBooksQuery);

        var raw = doc.RootElement.GetRawText();
        raw.Should().NotContain("Alice's First Book");
        raw.Should().NotContain("Alice's Second Book");
        raw.Should().NotContain("Bob's Only Book");
    }

    // ── JWT parity ──────────────────────────────────────────────────────

    [Test]
    public async Task DirectOwnedBooks_AsAdminJwt_Succeeds()
    {
        using var host = await StartAsync(Schemes.Jwt);
        var token = SignJwt("alice", "Alice", "Admin");

        using var doc = await host.PostGraphQLAsync(
            DirectOwnedBooksQuery,
            req => req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token)
        );

        AssertNoErrors(doc);
        OwnedBooksField(doc).GetProperty("totalCount").GetInt32().Should().Be(3);
    }

    [Test]
    public async Task DirectOwnedBooks_AsPlayerJwt_ReturnsAuthorizationError()
    {
        using var host = await StartAsync(Schemes.Jwt);
        var token = SignJwt("alice", "Alice", "Player");

        using var doc = await host.PostGraphQLAsync(
            DirectOwnedBooksQuery,
            req => req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token)
        );

        AssertTraxAuthorizationError(doc);
    }

    [Test]
    public async Task TransitiveBooks_AsPlayerJwt_FailsAuthorizationOnChildren()
    {
        using var host = await StartAsync(Schemes.Jwt);
        var token = SignJwt("alice", "Alice", "Player");

        using var doc = await host.PostGraphQLAsync(
            OwnersWithBooksQuery,
            req => req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token)
        );

        AssertTraxAuthorizationError(doc);
    }

    // ── CSV roles (OR within attribute) ──────────────────────────────────

    [Test]
    public async Task CsvRoles_AsAdmin_Succeeds()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            MemosQuery,
            req => req.Headers.Add("X-Api-Key", AdminApiKey)
        );

        AssertNoErrors(doc);
        VaultField(doc, "memos").GetProperty("totalCount").GetInt32().Should().Be(2);
    }

    [Test]
    public async Task CsvRoles_AsPlayer_Succeeds()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            MemosQuery,
            req => req.Headers.Add("X-Api-Key", PlayerApiKey)
        );

        AssertNoErrors(doc);
        VaultField(doc, "memos").GetProperty("totalCount").GetInt32().Should().Be(2);
    }

    [Test]
    public async Task CsvRoles_Anonymous_ReturnsAuthorizationError()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(MemosQuery);

        AssertTraxAuthorizationError(doc);
    }

    // ── Stacked attributes (AND across) ──────────────────────────────────

    [Test]
    public async Task StackedAttributes_AsAdmin_SatisfiesBoth()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            RestrictedDocsQuery,
            req => req.Headers.Add("X-Api-Key", AdminApiKey)
        );

        AssertNoErrors(doc);
        VaultField(doc, "restrictedDocs").GetProperty("totalCount").GetInt32().Should().Be(1);
    }

    [Test]
    public async Task StackedAttributes_AsPlayer_FailsOnRole()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            RestrictedDocsQuery,
            req => req.Headers.Add("X-Api-Key", PlayerApiKey)
        );

        AssertTraxAuthorizationError(doc);
    }

    // ── Bare [TraxAuthorize] (any authenticated user) ───────────────────

    [Test]
    public async Task BareAuthorize_AsAdmin_Succeeds()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            MemberAreasQuery,
            req => req.Headers.Add("X-Api-Key", AdminApiKey)
        );

        AssertNoErrors(doc);
        VaultField(doc, "memberAreas").GetProperty("totalCount").GetInt32().Should().Be(1);
    }

    [Test]
    public async Task BareAuthorize_AsPlayer_Succeeds()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            MemberAreasQuery,
            req => req.Headers.Add("X-Api-Key", PlayerApiKey)
        );

        AssertNoErrors(doc);
        VaultField(doc, "memberAreas").GetProperty("totalCount").GetInt32().Should().Be(1);
    }

    [Test]
    public async Task BareAuthorize_Anonymous_ReturnsAuthorizationError()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(MemberAreasQuery);

        AssertTraxAuthorizationError(doc);
    }

    // ── Connection-shape side channels (totalCount, pageInfo) ───────────
    //
    // Type-level @authorize alone is not enough: a request that selects only
    // Connection scalars like `totalCount` or `pageInfo` never resolves a
    // node of the gated entity type, so the type-level directive never fires.
    // The implementation also gates the entry field, which closes the
    // side channel. These tests pin the closure: they MUST fail for an
    // unauthorized caller, even though they ask for nothing about the entity
    // itself.

    [Test]
    public async Task DirectOwnedBooks_TotalCountOnly_AsPlayer_StillBlocked()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            "{ discover { vault { ownedBooks { totalCount } } } }",
            req => req.Headers.Add("X-Api-Key", PlayerApiKey)
        );

        AssertTraxAuthorizationError(doc);
    }

    [Test]
    public async Task DirectOwnedBooks_PageInfoOnly_AsPlayer_StillBlocked()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            "{ discover { vault { ownedBooks { pageInfo { hasNextPage } } } } }",
            req => req.Headers.Add("X-Api-Key", PlayerApiKey)
        );

        AssertTraxAuthorizationError(doc);
    }

    [Test]
    public async Task DirectOwnedBooks_TotalCountOnly_Anonymous_StillBlocked()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            "{ discover { vault { ownedBooks { totalCount } } } }"
        );

        AssertTraxAuthorizationError(doc);
    }

    [Test]
    public async Task DirectOwnedBooks_EdgesCursorOnly_AsPlayer_StillBlocked()
    {
        // edges.cursor doesn't materialize a node, but the entry field itself
        // is gated — the request must still be denied.
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            "{ discover { vault { ownedBooks { edges { cursor } } } } }",
            req => req.Headers.Add("X-Api-Key", PlayerApiKey)
        );

        AssertTraxAuthorizationError(doc);
    }

    [Test]
    public async Task DirectOwnedBooks_FilteredCount_AsPlayer_StillBlocked()
    {
        // totalCount with a `where` clause could otherwise be used to probe
        // for the presence of rows matching arbitrary predicates (e.g.,
        // "does a book titled 'secret' exist?"). Field-level auth closes
        // that probe.
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            "{ discover { vault { ownedBooks(where: { title: { contains: \"secret\" } }) { totalCount } } } }",
            req => req.Headers.Add("X-Api-Key", PlayerApiKey)
        );

        AssertTraxAuthorizationError(doc);
    }

    [Test]
    public async Task TransitiveBooks_TypenameOnly_AsPlayer_StillBlocked()
    {
        // Even __typename on a transitively reached gated type must fail —
        // the type-level directive should fire before any field on the
        // gated type is exposed, including the meta __typename field.
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            "{ discover { vault { owners { nodes { name books { __typename } } } } } }",
            req => req.Headers.Add("X-Api-Key", PlayerApiKey)
        );

        AssertTraxAuthorizationError(doc);
    }

    // ── Error-shape / opacity guards ─────────────────────────────────────

    [Test]
    public async Task ErrorMessage_DoesNotLeakEntityNameOrRoleName()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            DirectOwnedBooksQuery,
            req => req.Headers.Add("X-Api-Key", PlayerApiKey)
        );

        var raw = doc.RootElement.GetRawText();
        raw.Should().NotContain("OwnedBook");
        raw.Should().NotContain("Admin", "role name should not leak in the error body");
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static JsonElement OwnedBooksField(JsonDocument doc) => VaultField(doc, "ownedBooks");

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
