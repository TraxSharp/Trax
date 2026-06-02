using Trax.Samples.Bookworm.Auth;
using Trax.Samples.Bookworm.E2E.Fixtures;
using Trax.Samples.Bookworm.E2E.Utilities;

namespace Trax.Samples.Bookworm.E2E.ApiTests;

/// <summary>
/// Verifies that the <c>[TraxAuthorize(Roles = Member)]</c> gate on the borrow mutation is enforced
/// through the real HTTP + auth pipeline.
/// </summary>
[TestFixture]
public class AuthTests : ApiTestFixture
{
    private const string BorrowMutation =
        "mutation { dispatch { lending { borrowBook(input: { memberId: 1, bookId: 1 }) "
        + "{ externalId } } } }";

    [Test]
    public async Task BorrowBook_Anonymous_IsRejected()
    {
        var doc = await GraphQL.PostAsync(BorrowMutation);

        GraphQLClient
            .HasErrors(doc)
            .Should()
            .BeTrue("an unauthenticated caller must not be able to borrow a book");
    }

    [Test]
    public async Task BorrowBook_AuthenticatedMember_Succeeds()
    {
        var doc = await GraphQL.PostAsync(BorrowMutation, ApiKeyDefaults.MemberKey);

        GraphQLClient.HasErrors(doc).Should().BeFalse("a member is authorized to borrow a book");

        doc.RootElement.GetProperty("data")
            .GetProperty("dispatch")
            .GetProperty("lending")
            .GetProperty("borrowBook")
            .GetProperty("externalId")
            .GetString()
            .Should()
            .NotBeNullOrEmpty();
    }
}
