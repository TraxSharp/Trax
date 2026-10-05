using System.Text.Json;
using Trax.Samples.Bookworm.Auth;
using Trax.Samples.Bookworm.E2E.Fixtures;
using Trax.Samples.Bookworm.E2E.Utilities;

namespace Trax.Samples.Bookworm.E2E.ApiTests;

/// <summary>
/// Exercises the return-loan mutation end to end: <c>ReturnBookTrain</c> through
/// <c>ReturnBookJunction</c> stamps <c>returned_at</c> in the lending schema. Covers the
/// <c>[TraxAuthorize]</c> gate, the happy path with persistence, and the already-returned and
/// unknown-loan guard branches.
/// </summary>
[TestFixture]
public class ReturnBookTests : ApiTestFixture
{
    // Borrows a book no other test has on loan and returns the loan id, so each test owns the loan
    // it returns.
    private async Task<int> BorrowLoanAsync() =>
        await BorrowAsync(await NewBookAsync(), ApiKeyDefaults.MemberKey);

    private static string ReturnMutation(int loanId) =>
        $"mutation {{ dispatch {{ lending {{ returnBook(input: {{ loanId: {loanId} }}) "
        + "{ output { loanId returnedAt } } } } }";

    [Test]
    public async Task ReturnBook_AuthenticatedMember_StampsReturnedAtAndPersists()
    {
        var loanId = await BorrowLoanAsync();

        var doc = await GraphQL.PostAsync(ReturnMutation(loanId), ApiKeyDefaults.MemberKey);

        GraphQLClient.HasErrors(doc).Should().BeFalse("a member may return a loan");

        var result = doc
            .RootElement.GetProperty("data")
            .GetProperty("dispatch")
            .GetProperty("lending")
            .GetProperty("returnBook")
            .GetProperty("output");

        result.GetProperty("loanId").GetInt32().Should().Be(loanId);
        result.GetProperty("returnedAt").GetDateTime().Should().BeOnOrBefore(DateTime.UtcNow);

        // The stamp persists: re-reading this exact loan through the query model (filtered by id, so
        // pagination can't hide it) shows a non-null returnedAt.
        var readBack = await GraphQL.PostAsync(
            "{ discover { lending { loans(where: { id: { eq: "
                + loanId
                + " } }) { nodes { id returnedAt } } } } }",
            ApiKeyDefaults.MemberKey
        );
        var nodes = readBack
            .RootElement.GetProperty("data")
            .GetProperty("discover")
            .GetProperty("lending")
            .GetProperty("loans")
            .GetProperty("nodes");
        nodes.GetArrayLength().Should().Be(1, "the borrowed loan should be queryable by its id");
        nodes[0]
            .GetProperty("returnedAt")
            .ValueKind.Should()
            .NotBe(JsonValueKind.Null, "the returned loan must report its returnedAt");
    }

    [Test]
    public async Task ReturnBook_AlreadyReturned_ReturnsError()
    {
        var loanId = await BorrowLoanAsync();

        var first = await GraphQL.PostAsync(ReturnMutation(loanId), ApiKeyDefaults.MemberKey);
        GraphQLClient.HasErrors(first).Should().BeFalse("the first return should succeed");

        var second = await GraphQL.PostAsync(ReturnMutation(loanId), ApiKeyDefaults.MemberKey);
        GraphQLClient
            .HasErrors(second)
            .Should()
            .BeTrue("returning a loan twice must surface the already-returned guard");
    }

    [Test]
    public async Task ReturnBook_UnknownLoan_ReturnsError()
    {
        var doc = await GraphQL.PostAsync(
            ReturnMutation(loanId: 999_999),
            ApiKeyDefaults.MemberKey
        );

        GraphQLClient
            .HasErrors(doc)
            .Should()
            .BeTrue("returning a non-existent loan must surface the not-found guard");
    }

    [Test]
    public async Task ReturnBook_Anonymous_IsRejected()
    {
        var loanId = await BorrowLoanAsync();

        var doc = await GraphQL.PostAsync(ReturnMutation(loanId)); // no API key

        GraphQLClient
            .FirstError(doc)
            .Should()
            .Be("Not authorized.", "an unauthenticated caller must not be able to return a book");
    }
}
