using System.Text.Json;
using Trax.Samples.Bookworm.Auth;
using Trax.Samples.Bookworm.E2E.Fixtures;
using Trax.Samples.Bookworm.E2E.Utilities;

namespace Trax.Samples.Bookworm.E2E.ApiTests;

/// <summary>
/// Exercises the catalog search query end to end: <c>SearchCatalogTrain</c> through
/// <c>SearchCatalogJunction</c>'s case-insensitive ILIKE against the catalog schema, projected to the
/// flat <c>BookSummary</c> output type.
/// </summary>
[TestFixture]
public class CatalogSearchTests : ApiTestFixture
{
    [Test]
    public async Task SearchCatalog_LowercaseQuery_MatchesSeededTitleCaseInsensitively()
    {
        // The host seeds "The Hobbit"; a lowercase query must still match it via ILIKE.
        var doc = await GraphQL.PostAsync(
            "{ discover { catalog { searchCatalog(input: { query: \"hobbit\" }) "
                + "{ books { id title isbn } } } } }",
            ApiKeyDefaults.MemberKey
        );

        GraphQLClient.HasErrors(doc).Should().BeFalse("the catalog search query should succeed");

        var books = doc
            .RootElement.GetProperty("data")
            .GetProperty("discover")
            .GetProperty("catalog")
            .GetProperty("searchCatalog")
            .GetProperty("books");

        var hobbit = books
            .EnumerateArray()
            .FirstOrDefault(b => b.GetProperty("title").GetString() == "The Hobbit");

        hobbit
            .ValueKind.Should()
            .Be(JsonValueKind.Object, "the seeded Hobbit must match the search");
        hobbit.GetProperty("id").GetInt32().Should().BeGreaterThan(0);
        hobbit.GetProperty("isbn").GetString().Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task SearchCatalog_PartialSubstring_MatchesOnlyTheExpectedTitle()
    {
        // "earthsea" is a substring of exactly one seeded title; it must match that one and not the
        // unrelated Hobbit row, proving the ILIKE is a real substring filter and not a pass-through.
        var doc = await GraphQL.PostAsync(
            "{ discover { catalog { searchCatalog(input: { query: \"earthsea\" }) "
                + "{ books { title } } } } }",
            ApiKeyDefaults.MemberKey
        );

        GraphQLClient.HasErrors(doc).Should().BeFalse();

        var titles = doc
            .RootElement.GetProperty("data")
            .GetProperty("discover")
            .GetProperty("catalog")
            .GetProperty("searchCatalog")
            .GetProperty("books")
            .EnumerateArray()
            .Select(b => b.GetProperty("title").GetString())
            .ToList();

        titles.Should().Contain("A Wizard of Earthsea");
        titles.Should().NotContain("The Hobbit");
    }

    [Test]
    public async Task SearchCatalog_NoMatch_ReturnsEmptyList()
    {
        var doc = await GraphQL.PostAsync(
            "{ discover { catalog { searchCatalog(input: { query: \"no-such-title-zzz\" }) "
                + "{ books { id } } } } }",
            ApiKeyDefaults.MemberKey
        );

        GraphQLClient.HasErrors(doc).Should().BeFalse();

        var books = doc
            .RootElement.GetProperty("data")
            .GetProperty("discover")
            .GetProperty("catalog")
            .GetProperty("searchCatalog")
            .GetProperty("books");

        books.ValueKind.Should().Be(JsonValueKind.Array);
        books.GetArrayLength().Should().Be(0, "no catalog title matches the query");
    }
}
