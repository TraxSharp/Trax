using System.Text.Json;
using Trax.Samples.Bookworm.Auth;
using Trax.Samples.Bookworm.E2E.Fixtures;
using Trax.Samples.Bookworm.E2E.Utilities;

namespace Trax.Samples.Bookworm.E2E.ApiTests;

/// <summary>
/// Exercises the loan -> book cross-schema GraphQL edge end to end: a loan in the lending schema
/// resolves the book it references in the catalog schema, through the batched cross-schema loader.
/// This is the mandated integration test for the <c>Loan.book</c> edge.
/// </summary>
[TestFixture]
public class CrossSchemaEdgeTests : ApiTestFixture
{
    [Test]
    public async Task Loan_book_edge_ResolvesCatalogBookAcrossSchemas()
    {
        var doc = await GraphQL.PostAsync(
            "{ discover { lending { loans { nodes { id bookId book { title isbn } } } } } }",
            ApiKeyDefaults.MemberKey
        );

        GraphQLClient.HasErrors(doc).Should().BeFalse("the cross-schema edge query should succeed");

        var nodes = doc
            .RootElement.GetProperty("data")
            .GetProperty("discover")
            .GetProperty("lending")
            .GetProperty("loans")
            .GetProperty("nodes");

        nodes.GetArrayLength().Should().BeGreaterThan(0, "the host seeds one loan");

        var loan = nodes[0];
        var book = loan.GetProperty("book");
        book.ValueKind.Should().Be(JsonValueKind.Object, "the loan's catalog book must resolve");
        book.GetProperty("title").GetString().Should().Be("The Hobbit");
        book.GetProperty("isbn").GetString().Should().NotBeNullOrEmpty();
    }

    [Test]
    public async Task Loan_book_edge_ResolvesConsistentlyAcrossEveryLoan()
    {
        // Borrow the second catalog book so more than one loan exists, then confirm every loan's
        // cross-schema book resolves to a non-null catalog row (the batched loader keys correctly).
        await GraphQL.PostAsync(
            "mutation { dispatch { lending { borrowBook(input: { memberId: 1, bookId: 2 }) "
                + "{ externalId } } } }",
            ApiKeyDefaults.MemberKey
        );

        var doc = await GraphQL.PostAsync(
            "{ discover { lending { loans { nodes { bookId book { id title } } } } } }",
            ApiKeyDefaults.MemberKey
        );

        GraphQLClient.HasErrors(doc).Should().BeFalse();

        var nodes = doc
            .RootElement.GetProperty("data")
            .GetProperty("discover")
            .GetProperty("lending")
            .GetProperty("loans")
            .GetProperty("nodes");

        foreach (var loan in nodes.EnumerateArray())
        {
            var bookId = loan.GetProperty("bookId").GetInt32();
            var book = loan.GetProperty("book");
            book.ValueKind.Should().Be(JsonValueKind.Object);
            book.GetProperty("id").GetInt32().Should().Be(bookId);
        }
    }
}
