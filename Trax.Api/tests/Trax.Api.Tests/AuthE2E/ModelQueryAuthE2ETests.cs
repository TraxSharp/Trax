using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.TestHost;
using static Trax.Api.Tests.AuthE2E.AuthE2EHost;

namespace Trax.Api.Tests.AuthE2E;

/// <summary>
/// End-to-end coverage for <c>[TraxQueryModel]</c>-generated GraphQL queries
/// backed by real EF Core + PostgreSQL, exercised through every Trax auth
/// scheme. Proves that the paged / filtered / sorted / projected model
/// queries propagate authentication correctly and that protected endpoints
/// reject bad credentials before any SQL runs.
///
/// Uses the isolated <c>test_auth</c> schema seeded by <see cref="TestDbContext"/>
/// so fixture rows are deterministic and don't collide with other samples.
/// </summary>
[TestFixture]
[NonParallelizable]
public class ModelQueryAuthE2ETests
{
    private const string Database = "trax_api_auth_model";

    private static Task<Microsoft.Extensions.Hosting.IHost> StartAsync(Schemes s) =>
        AuthE2EHost.StartAsync(s, Database);

    [OneTimeSetUp]
    public void SeedDatabase() =>
        TestDbContext.EnsureSeeded(AuthE2EHost.ConnectionString(Database));

    // ── Queries ───────────────────────────────────────────────────────────

    private const string CountQuery = """
        { discover { library { bookRecords { totalCount } } } }
        """;

    private const string ListAllQuery = """
        { discover { library { bookRecords { nodes { title author rating } } } } }
        """;

    private const string FilteredQuery = """
        {
          discover {
            library {
              bookRecords(where: { rating: { eq: 5 } }) {
                totalCount
                nodes { title author }
              }
            }
          }
        }
        """;

    private const string SortedQuery = """
        {
          discover {
            library {
              bookRecords(order: { title: ASC }, first: 3) {
                nodes { title }
              }
            }
          }
        }
        """;

    private const string PagedQuery = """
        {
          discover {
            library {
              bookRecords(first: 2) {
                nodes { title }
                pageInfo { hasNextPage endCursor }
              }
            }
          }
        }
        """;

    private const string NestedFilterQuery = """
        {
          discover {
            library {
              bookRecords(
                where: { and: [{ rating: { gte: 4 } }, { author: { neq: "Martin" } }] }
                order: { author: ASC }
              ) {
                totalCount
                nodes { author title rating }
              }
            }
          }
        }
        """;

    // ── Basic query propagation (per scheme) ─────────────────────────────

    [Test]
    public async Task Count_WithApiKey_Succeeds()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            CountQuery,
            req => req.Headers.Add("X-Api-Key", PlayerApiKey)
        );

        AssertNoErrors(doc);
        BookRecordsField(doc).GetProperty("totalCount").GetInt32().Should().Be(8);
    }

    [Test]
    public async Task Count_WithJwt_Succeeds()
    {
        using var host = await StartAsync(Schemes.Jwt);
        var token = SignJwt("alice", "Alice", "Player");

        using var doc = await host.PostGraphQLAsync(
            CountQuery,
            req => req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token)
        );

        AssertNoErrors(doc);
        BookRecordsField(doc).GetProperty("totalCount").GetInt32().Should().Be(8);
    }

    [Test]
    public async Task ListAll_AnonymousOnUnprotectedEndpoint_ReturnsRows()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(ListAllQuery);

        AssertNoErrors(doc);
        BookRecordsField(doc).GetProperty("nodes").GetArrayLength().Should().Be(8);
    }

    // ── Protected endpoint rejects bad credentials before SQL runs ──────

    [Test]
    public async Task ListAll_InvalidApiKey_OnProtectedEndpoint_Returns401()
    {
        using var host = await StartAsync(Schemes.ApiKey);
        var client = host.GetTestServer().CreateClient();

        using var req = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql/protected")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { query = ListAllQuery }),
        };
        req.Headers.Add("X-Api-Key", "bogus-key");

        var res = await client.SendAsync(req);

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task ListAll_InvalidJwt_OnProtectedEndpoint_Returns401()
    {
        using var host = await StartAsync(Schemes.Jwt);
        var client = host.GetTestServer().CreateClient();

        using var req = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql/protected")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { query = ListAllQuery }),
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not.a.jwt");

        var res = await client.SendAsync(req);

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task ListAll_NoCredentials_OnProtectedEndpoint_Returns401()
    {
        using var host = await StartAsync(Schemes.ApiKey);
        var client = host.GetTestServer().CreateClient();

        using var req = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql/protected")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { query = ListAllQuery }),
        };

        var res = await client.SendAsync(req);

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Test]
    public async Task ListAll_ValidApiKey_OnProtectedEndpoint_Succeeds()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostProtectedGraphQLAsync(
            ListAllQuery,
            req => req.Headers.Add("X-Api-Key", AdminApiKey)
        );

        AssertNoErrors(doc);
        BookRecordsField(doc).GetProperty("nodes").GetArrayLength().Should().Be(8);
    }

    [Test]
    public async Task ListAll_ValidJwt_OnProtectedEndpoint_Succeeds()
    {
        using var host = await StartAsync(Schemes.Jwt);
        var token = SignJwt("alice", "Alice", "Player");

        using var doc = await host.PostProtectedGraphQLAsync(
            ListAllQuery,
            req => req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token)
        );

        AssertNoErrors(doc);
        BookRecordsField(doc).GetProperty("nodes").GetArrayLength().Should().Be(8);
    }

    // ── Filtering / sorting / paging survive through auth pipeline ──────

    [Test]
    public async Task Filtered_WithApiKey_OnlyMatches()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            FilteredQuery,
            req => req.Headers.Add("X-Api-Key", PlayerApiKey)
        );

        AssertNoErrors(doc);
        var field = BookRecordsField(doc);
        field.GetProperty("totalCount").GetInt32().Should().Be(3);
        foreach (var node in field.GetProperty("nodes").EnumerateArray())
        {
            node.GetProperty("title").GetString().Should().NotBeNullOrEmpty();
        }
    }

    [Test]
    public async Task Filtered_WithJwt_OnlyMatches()
    {
        using var host = await StartAsync(Schemes.Jwt);
        var token = SignJwt("alice", "Alice", "Player");

        using var doc = await host.PostGraphQLAsync(
            FilteredQuery,
            req => req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token)
        );

        AssertNoErrors(doc);
        BookRecordsField(doc).GetProperty("totalCount").GetInt32().Should().Be(3);
    }

    [Test]
    public async Task Sorted_WithApiKey_AppliesOrder()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            SortedQuery,
            req => req.Headers.Add("X-Api-Key", PlayerApiKey)
        );

        AssertNoErrors(doc);
        var titles = BookRecordsField(doc)
            .GetProperty("nodes")
            .EnumerateArray()
            .Select(n => n.GetProperty("title").GetString())
            .ToArray();
        titles.Should().HaveCount(3);
        titles.Should().BeInAscendingOrder();
    }

    [Test]
    public async Task Paged_WithJwt_ReturnsCursor()
    {
        using var host = await StartAsync(Schemes.Jwt);
        var token = SignJwt("alice", "Alice", "Player");

        using var doc = await host.PostGraphQLAsync(
            PagedQuery,
            req => req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token)
        );

        AssertNoErrors(doc);
        var field = BookRecordsField(doc);
        field.GetProperty("nodes").GetArrayLength().Should().Be(2);
        field.GetProperty("pageInfo").GetProperty("hasNextPage").GetBoolean().Should().BeTrue();
        field
            .GetProperty("pageInfo")
            .GetProperty("endCursor")
            .GetString()
            .Should()
            .NotBeNullOrEmpty();
    }

    [Test]
    public async Task NestedFilter_WithApiKey_ReturnsCorrectlyFilteredAndSorted()
    {
        using var host = await StartAsync(Schemes.ApiKey);

        using var doc = await host.PostGraphQLAsync(
            NestedFilterQuery,
            req => req.Headers.Add("X-Api-Key", AdminApiKey)
        );

        AssertNoErrors(doc);
        var field = BookRecordsField(doc);
        // rating >= 4 ⇒ 7 rows, minus Martin (Clean Code is rating 3 already
        // excluded) leaves the 7 non-Martin 4+ books. Rating 5: Brooks,
        // Abelson, Kernighan. Rating 4: Gamma, Fowler, Feathers, Evans.
        field.GetProperty("totalCount").GetInt32().Should().Be(7);

        var authors = field
            .GetProperty("nodes")
            .EnumerateArray()
            .Select(n => n.GetProperty("author").GetString())
            .ToArray();
        authors.Should().NotContain("Martin");
        authors.Should().BeInAscendingOrder();
    }

    // ── Multi-scheme model queries ──────────────────────────────────────

    [Test]
    public async Task BothSchemes_ApiKey_OnProtectedEndpoint_Succeeds()
    {
        using var host = await StartAsync(Schemes.ApiKey | Schemes.Jwt);

        using var doc = await host.PostProtectedGraphQLAsync(
            CountQuery,
            req => req.Headers.Add("X-Api-Key", PlayerApiKey)
        );

        AssertNoErrors(doc);
        BookRecordsField(doc).GetProperty("totalCount").GetInt32().Should().Be(8);
    }

    [Test]
    public async Task BothSchemes_Jwt_OnProtectedEndpoint_Succeeds()
    {
        using var host = await StartAsync(Schemes.ApiKey | Schemes.Jwt);
        var token = SignJwt("alice", "Alice", "Player");

        using var doc = await host.PostProtectedGraphQLAsync(
            CountQuery,
            req => req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token)
        );

        AssertNoErrors(doc);
        BookRecordsField(doc).GetProperty("totalCount").GetInt32().Should().Be(8);
    }

    [Test]
    public async Task BothSchemes_NoCredentials_OnProtectedEndpoint_Returns401()
    {
        using var host = await StartAsync(Schemes.ApiKey | Schemes.Jwt);
        var client = host.GetTestServer().CreateClient();

        using var req = new HttpRequestMessage(HttpMethod.Post, "/trax/graphql/protected")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { query = CountQuery }),
        };

        var res = await client.SendAsync(req);

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── Regression: concurrent model queries with different principals ──

    [Test]
    public async Task Concurrent_MultipleAuthedRequests_DoNotCrossContaminate()
    {
        using var host = await StartAsync(Schemes.ApiKey);
        var tasks = Enumerable
            .Range(0, 20)
            .Select(async i =>
            {
                var key = i % 2 == 0 ? AdminApiKey : PlayerApiKey;
                using var doc = await host.PostGraphQLAsync(
                    CountQuery,
                    req => req.Headers.Add("X-Api-Key", key)
                );
                AssertNoErrors(doc);
                return BookRecordsField(doc).GetProperty("totalCount").GetInt32();
            });

        var counts = await Task.WhenAll(tasks);
        counts.Should().AllBeEquivalentTo(8);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    private static JsonElement BookRecordsField(JsonDocument doc) =>
        doc
            .RootElement.GetProperty("data")
            .GetProperty("discover")
            .GetProperty("library")
            .GetProperty("bookRecords");

    private static void AssertNoErrors(JsonDocument doc) =>
        doc
            .RootElement.TryGetProperty("errors", out _)
            .Should()
            .BeFalse(doc.RootElement.GetRawText());
}
