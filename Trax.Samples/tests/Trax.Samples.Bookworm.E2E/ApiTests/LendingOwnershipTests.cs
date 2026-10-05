using System.Text.Json;
using Trax.Samples.Bookworm.Auth;
using Trax.Samples.Bookworm.E2E.Fixtures;
using Trax.Samples.Bookworm.E2E.Utilities;

namespace Trax.Samples.Bookworm.E2E.ApiTests;

/// <summary>
/// The cross-user behavioural tests the owner-scope census asks for: members and loans are
/// per-member rows, so each surface they are read or changed through gives one member nothing of
/// another's, a librarian everything, and an anonymous caller nothing at all.
/// </summary>
[TestFixture]
public class LendingOwnershipTests : ApiTestFixture
{
    private const string MembersQuery =
        "{ discover { lending { members { nodes { name email } } } } }";

    private static IReadOnlyList<string> Names(JsonDocument doc, string field) =>
        doc
            .RootElement.GetProperty("data")
            .GetProperty("discover")
            .GetProperty("lending")
            .GetProperty(field)
            .GetProperty("nodes")
            .EnumerateArray()
            .Select(n => n.GetRawText())
            .ToList();

    [Test]
    public async Task Anonymous_caller_cannot_read_member_emails()
    {
        var doc = await GraphQL.PostAsync(MembersQuery);

        doc.RootElement.GetRawText().Should().NotContain("@example.com");
        doc.RootElement.GetProperty("errors")[0]
            .GetProperty("message")
            .GetString()
            .Should()
            .Be("Not authorized.");
    }

    [Test]
    public async Task Anonymous_caller_cannot_read_loans()
    {
        var doc = await GraphQL.PostAsync("{ discover { lending { loans { nodes { id } } } } }");

        doc.RootElement.GetProperty("errors")[0]
            .GetProperty("message")
            .GetString()
            .Should()
            .Be("Not authorized.");
    }

    [Test]
    public async Task A_member_reads_only_their_own_member_row()
    {
        var doc = await GraphQL.PostAsync(MembersQuery, ApiKeyDefaults.MemberKey);

        Names(doc, "members").Should().ContainSingle().Which.Should().Contain("ada@example.com");
    }

    [Test]
    public async Task A_librarian_reads_every_member()
    {
        var doc = await GraphQL.PostAsync(MembersQuery, ApiKeyDefaults.LibrarianKey);

        var members = Names(doc, "members");
        members.Should().Contain(m => m.Contains("ada@example.com"));
        members.Should().Contain(m => m.Contains("grace@example.com"));
    }

    [Test]
    public async Task A_member_reads_only_their_own_loans()
    {
        var gracesLoan = await BorrowAsync(await NewBookAsync(), ApiKeyDefaults.OtherMemberKey);

        var asAda = await GraphQL.PostAsync(
            "{ discover { lending { loans { nodes { id book { title } } } } } }",
            ApiKeyDefaults.MemberKey
        );
        var asGrace = await GraphQL.PostAsync(
            "{ discover { lending { loans { nodes { id book { title } } } } } }",
            ApiKeyDefaults.OtherMemberKey
        );

        GraphQLClient.HasErrors(asAda).Should().BeFalse(asAda.RootElement.GetRawText());
        Names(asAda, "loans").Should().NotContain(l => l.Contains($"\"id\":{gracesLoan},"));
        Names(asGrace, "loans").Should().Contain(l => l.Contains($"\"id\":{gracesLoan},"));
    }

    [Test]
    public async Task A_member_cannot_return_another_members_loan()
    {
        var gracesLoan = await BorrowAsync(await NewBookAsync(), ApiKeyDefaults.OtherMemberKey);

        var doc = await GraphQL.PostAsync(
            $"mutation {{ dispatch {{ lending {{ returnBook(input: {{ loanId: {gracesLoan} }}) {{ output {{ loanId }} }} }} }} }}",
            ApiKeyDefaults.MemberKey
        );

        // The loan is outside Ada's filter, so to her it does not exist.
        doc.RootElement.GetProperty("errors")[0]
            .GetProperty("message")
            .GetString()
            .Should()
            .Be($"Loan {gracesLoan} not found.");
    }

    [Test]
    public async Task A_librarian_can_return_any_loan()
    {
        var gracesLoan = await BorrowAsync(await NewBookAsync(), ApiKeyDefaults.OtherMemberKey);

        var doc = await GraphQL.PostAsync(
            $"mutation {{ dispatch {{ lending {{ returnBook(input: {{ loanId: {gracesLoan} }}) {{ output {{ loanId }} }} }} }} }}",
            ApiKeyDefaults.LibrarianKey
        );

        GraphQLClient.HasErrors(doc).Should().BeFalse(doc.RootElement.GetRawText());
    }
}
