using Trax.Samples.Bookworm.Auth;
using Trax.Samples.Bookworm.E2E.Fixtures;
using Trax.Samples.Bookworm.E2E.Utilities;

namespace Trax.Samples.Bookworm.E2E.ApiTests;

/// <summary>
/// A loan is recorded only for a book that exists in the catalog and is on the shelf, and only for
/// the calling member.
/// </summary>
[TestFixture]
public class LendingIntegrityTests : ApiTestFixture
{
    [Test]
    public async Task A_book_already_on_loan_cannot_be_borrowed_again()
    {
        var bookId = await NewBookAsync();
        await BorrowAsync(bookId, ApiKeyDefaults.MemberKey);

        var second = await GraphQL.PostAsync(Borrow(bookId), ApiKeyDefaults.OtherMemberKey);

        GraphQLClient.FirstError(second).Should().Be($"Book {bookId} is already on loan.");
    }

    [Test]
    public async Task A_book_that_does_not_exist_cannot_be_borrowed()
    {
        var doc = await GraphQL.PostAsync(Borrow(bookId: 987654), ApiKeyDefaults.MemberKey);

        GraphQLClient.FirstError(doc).Should().Be("Book 987654 is not in the catalog.");
    }

    [Test]
    public async Task A_returned_book_can_be_borrowed_again()
    {
        var bookId = await NewBookAsync();
        var loanId = await BorrowAsync(bookId, ApiKeyDefaults.MemberKey);
        var returned = await GraphQL.PostAsync(
            $"mutation {{ dispatch {{ lending {{ returnBook(input: {{ loanId: {loanId} }}) {{ output {{ loanId }} }} }} }} }}",
            ApiKeyDefaults.MemberKey
        );
        GraphQLClient.HasErrors(returned).Should().BeFalse(returned.RootElement.GetRawText());

        var again = await BorrowAsync(bookId, ApiKeyDefaults.OtherMemberKey);

        again.Should().NotBe(loanId);
    }

    [Test]
    public async Task Concurrent_borrows_of_one_book_record_one_loan()
    {
        var bookId = await NewBookAsync();

        // Both requests can pass the availability check before either inserts; the partial unique
        // index on open loans is what lets only one of them commit.
        var attempts = await Task.WhenAll(
            Enumerable
                .Range(0, 6)
                .Select(i =>
                    GraphQL.PostAsync(
                        Borrow(bookId),
                        i % 2 == 0 ? ApiKeyDefaults.MemberKey : ApiKeyDefaults.OtherMemberKey
                    )
                )
        );

        attempts.Count(a => !GraphQLClient.HasErrors(a)).Should().Be(1);
        attempts
            .Where(GraphQLClient.HasErrors)
            .Select(GraphQLClient.FirstError)
            .Should()
            .AllBe($"Book {bookId} is already on loan.");
    }

    [Test]
    public async Task A_loan_belongs_to_the_calling_member()
    {
        var bookId = await NewBookAsync();
        var loanId = await BorrowAsync(bookId, ApiKeyDefaults.OtherMemberKey);

        // The librarian reads every loan and member, so it can check who the loan was recorded for.
        var doc = await GraphQL.PostAsync(
            "{ discover { lending { loans(where: { id: { eq: "
                + loanId
                + " } }) { nodes { memberId } } members { nodes { id name } } } } }",
            ApiKeyDefaults.LibrarianKey
        );

        var lending = doc
            .RootElement.GetProperty("data")
            .GetProperty("discover")
            .GetProperty("lending");
        var memberId = lending
            .GetProperty("loans")
            .GetProperty("nodes")[0]
            .GetProperty("memberId")
            .GetInt32();
        lending
            .GetProperty("members")
            .GetProperty("nodes")
            .EnumerateArray()
            .Single(m => m.GetProperty("id").GetInt32() == memberId)
            .GetProperty("name")
            .GetString()
            .Should()
            .Be("Grace Hopper");
    }
}
